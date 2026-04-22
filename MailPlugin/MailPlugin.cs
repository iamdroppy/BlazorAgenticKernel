using System.ComponentModel;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.SemanticKernel;
using MimeKit;

namespace MailPlugin;

/// <summary>
/// Semantic Kernel plugin that speaks IMAP (read / search) and SMTP (send)
/// against any number of configured accounts.  The LLM picks an account by
/// its short name; <see cref="ListAccountsAsync"/> exposes the catalogue.
/// </summary>
public sealed class MailPlugin
{
    private const int DefaultCount = 10;
    private const int MaxCount = 50;
    private const int PreviewChars = 280;

    private readonly MailAccountRegistry _registry;

    public MailPlugin(MailAccountRegistry registry)
    {
        _registry = registry;
    }

    // -----------------------------------------------------------------
    // ListAccounts
    // -----------------------------------------------------------------
    [KernelFunction("ListAccounts")]
    [Description(
        "List the configured mail accounts.  Call this first if the user " +
        "doesn't name an account explicitly — each entry shows the short " +
        "name to pass to other Mail.* tools plus the display label.")]
    public string ListAccounts()
    {
        if (!_registry.HasAny) return "No mail accounts configured.";

        var sb = new StringBuilder();
        sb.AppendLine("Configured mail accounts:");
        foreach (var a in _registry.All)
        {
            sb.Append("- ").Append(a.Name);
            if (!string.IsNullOrWhiteSpace(a.DisplayName))
                sb.Append(" (").Append(a.DisplayName).Append(')');
            sb.Append("  imap=").Append(a.ImapHost)
              .Append("  smtp=").Append(a.SmtpHost)
              .Append("  from=").Append(a.ResolvedFrom);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    // -----------------------------------------------------------------
    // ReadInbox
    // -----------------------------------------------------------------
    [KernelFunction("ReadInbox")]
    [Description(
        "Read the most recent messages from the INBOX of a configured mail " +
        "account over IMAP.  Returns subject, sender, date, and a short " +
        "text snippet for each message, newest first.")]
    public async Task<string> ReadInboxAsync(
        [Description("Short name of the account (see Mail.ListAccounts).")]
        string account,
        [Description("How many messages to return (1-50). Default 10.")]
        int count = DefaultCount,
        CancellationToken cancellationToken = default)
    {
        var acc = Resolve(account, out var err);
        if (acc is null) return err!;
        count = Clamp(count);

        using var client = new ImapClient();
        try
        {
            await ConnectImapAsync(client, acc, cancellationToken);

            var inbox = client.Inbox!;
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            if (inbox.Count == 0)
            {
                await client.DisconnectAsync(true, cancellationToken);
                return $"[{acc.Name}] Inbox is empty.";
            }

            var start = Math.Max(0, inbox.Count - count);
            var end = inbox.Count - 1;
            var sb = new StringBuilder();
            sb.Append('[').Append(acc.Name).Append("] Showing ")
              .Append(end - start + 1).Append(" of ").Append(inbox.Count)
              .AppendLine(" messages (newest first):");

            // Newest first.
            for (int i = end; i >= start; i--)
            {
                var msg = await inbox.GetMessageAsync(i, cancellationToken);
                AppendSummary(sb, (uint)i, msg);
            }

            await client.DisconnectAsync(true, cancellationToken);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"[{acc.Name}] IMAP read failed: {ex.Message}";
        }
    }

    // -----------------------------------------------------------------
    // SearchInbox
    // -----------------------------------------------------------------
    [KernelFunction("SearchInbox")]
    [Description(
        "Search the INBOX of a configured mail account for messages that " +
        "match a free-text query (matches subject, body, from, or to).  " +
        "Returns the same summary format as Mail.ReadInbox.")]
    public async Task<string> SearchInboxAsync(
        [Description("Short name of the account (see Mail.ListAccounts).")]
        string account,
        [Description("Free-text query.  Matches subject, body, from, or to.")]
        string query,
        [Description("How many matches to return (1-50). Default 10.")]
        int count = DefaultCount,
        CancellationToken cancellationToken = default)
    {
        var acc = Resolve(account, out var err);
        if (acc is null) return err!;
        if (string.IsNullOrWhiteSpace(query))
            return $"[{acc.Name}] query is required.";
        count = Clamp(count);

        using var client = new ImapClient();
        try
        {
            await ConnectImapAsync(client, acc, cancellationToken);

            var inbox = client.Inbox!;
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var q = query.Trim();
            var searchQuery = SearchQuery.Or(
                SearchQuery.Or(
                    SearchQuery.SubjectContains(q),
                    SearchQuery.BodyContains(q)),
                SearchQuery.Or(
                    SearchQuery.FromContains(q),
                    SearchQuery.ToContains(q)));

            var uids = await inbox.SearchAsync(searchQuery, cancellationToken);
            if (uids.Count == 0)
            {
                await client.DisconnectAsync(true, cancellationToken);
                return $"[{acc.Name}] No messages match \"{q}\".";
            }

            // Newest first, cap to count.
            var take = uids.OrderByDescending(u => u.Id).Take(count).ToList();
            var sb = new StringBuilder();
            sb.Append('[').Append(acc.Name).Append("] ")
              .Append(take.Count).Append(" of ").Append(uids.Count)
              .Append(" matches for \"").Append(q).AppendLine("\":");

            foreach (var uid in take)
            {
                var msg = await inbox.GetMessageAsync(uid, cancellationToken);
                AppendSummary(sb, uid.Id, msg);
            }

            await client.DisconnectAsync(true, cancellationToken);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"[{acc.Name}] IMAP search failed: {ex.Message}";
        }
    }

    // -----------------------------------------------------------------
    // SendEmail
    // -----------------------------------------------------------------
    [KernelFunction("SendEmail")]
    [Description(
        "Send a plain-text email via SMTP using one of the configured " +
        "accounts.  Returns a short confirmation string, or an error " +
        "message starting with 'Error:' on failure.")]
    public async Task<string> SendEmailAsync(
        [Description("Short name of the account to send FROM (see Mail.ListAccounts).")]
        string account,
        [Description("Recipient address.  Comma-separate for multiple recipients.")]
        string to,
        [Description("Subject line.")]
        string subject,
        [Description("Plain-text body.")]
        string body,
        CancellationToken cancellationToken = default)
    {
        var acc = Resolve(account, out var err);
        if (acc is null) return err!;

        if (string.IsNullOrWhiteSpace(to))
            return $"[{acc.Name}] Error: 'to' is required.";

        MimeMessage message;
        try
        {
            message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(acc.ResolvedFrom));
            foreach (var recipient in SplitAddresses(to))
                message.To.Add(MailboxAddress.Parse(recipient));
            message.Subject = subject ?? "";
            message.Body = new TextPart("plain") { Text = body ?? "" };
        }
        catch (Exception ex)
        {
            return $"[{acc.Name}] Error: invalid address — {ex.Message}";
        }

        using var smtp = new SmtpClient();
        try
        {
            var secure = acc.SmtpUseStartTls
                ? SecureSocketOptions.StartTlsWhenAvailable
                : SecureSocketOptions.SslOnConnect;

            await smtp.ConnectAsync(acc.SmtpHost, acc.SmtpPort, secure, cancellationToken);
            if (!string.IsNullOrEmpty(acc.Username))
                await smtp.AuthenticateAsync(acc.Username, acc.Password, cancellationToken);

            var response = await smtp.SendAsync(message, cancellationToken);
            await smtp.DisconnectAsync(true, cancellationToken);

            return $"[{acc.Name}] Sent to {to} · subject \"{message.Subject}\" · server: {response}".TrimEnd();
        }
        catch (Exception ex)
        {
            return $"[{acc.Name}] Error: SMTP send failed — {ex.Message}";
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------
    private MailAccountOptions? Resolve(string account, out string? error)
    {
        if (!_registry.HasAny)
        {
            error = "No mail accounts are configured on the server.";
            return null;
        }

        var acc = _registry.Find(account);
        if (acc is null)
        {
            var known = string.Join(", ", _registry.All.Select(a => a.Name));
            error = string.IsNullOrWhiteSpace(account)
                ? $"Error: 'account' is required. Known accounts: {known}."
                : $"Error: unknown account '{account}'. Known accounts: {known}.";
            return null;
        }

        error = null;
        return acc;
    }

    private static async Task ConnectImapAsync(
        ImapClient client, MailAccountOptions acc, CancellationToken ct)
    {
        var secure = acc.ImapUseSsl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        await client.ConnectAsync(acc.ImapHost, acc.ImapPort, secure, ct);
        await client.AuthenticateAsync(acc.Username, acc.Password, ct);
    }

    private static void AppendSummary(StringBuilder sb, uint id, MimeMessage msg)
    {
        var from = msg.From.Mailboxes.FirstOrDefault();
        var fromLabel = from is null
            ? "(unknown)"
            : (!string.IsNullOrWhiteSpace(from.Name) ? $"{from.Name} <{from.Address}>" : from.Address);

        sb.Append("  #").Append(id).Append(' ')
          .Append(msg.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
          .Append("  from: ").Append(fromLabel).AppendLine();
        sb.Append("    subject: ").AppendLine(string.IsNullOrWhiteSpace(msg.Subject) ? "(no subject)" : msg.Subject);

        var body = msg.TextBody ?? msg.HtmlBody ?? "";
        body = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (body.Length > PreviewChars) body = body.Substring(0, PreviewChars) + "…";
        if (body.Length > 0) sb.Append("    ").AppendLine(body);
    }

    private static IEnumerable<string> SplitAddresses(string to) =>
        to.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int Clamp(int count) =>
        count <= 0 ? DefaultCount : Math.Min(count, MaxCount);
}

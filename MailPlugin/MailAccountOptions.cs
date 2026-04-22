namespace MailPlugin;

/// <summary>
/// Configuration for a single mail account — bound from
/// <c>Mail:Accounts</c> in appsettings / user-secrets / env vars.
///
/// Each account pairs IMAP (read) with SMTP (send), addressed by
/// <see cref="Name"/> so the LLM can route per-account.  Add as many
/// accounts as you like; the agent picks one via the <c>account</c>
/// argument on every Mail.* KernelFunction.
/// </summary>
public sealed class MailAccountOptions
{
    /// <summary>Unique short name — the LLM uses this to select the account.</summary>
    public string Name { get; set; } = "";

    /// <summary>Human-friendly label, e.g. "Work Gmail".</summary>
    public string DisplayName { get; set; } = "";

    // ---------- IMAP ----------
    public string ImapHost { get; set; } = "";
    public int    ImapPort { get; set; } = 993;
    public bool   ImapUseSsl { get; set; } = true;

    // ---------- SMTP ----------
    public string SmtpHost { get; set; } = "";
    public int    SmtpPort { get; set; } = 587;
    /// <summary>Use STARTTLS on the SMTP port (usual for 587). For port 465 set this to false and ImapUseSsl/SmtpUseSsl semantics handle it.</summary>
    public bool   SmtpUseStartTls { get; set; } = true;

    // ---------- Credentials ----------
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>From-address used on outgoing messages. Defaults to Username if empty.</summary>
    public string FromAddress { get; set; } = "";

    public string ResolvedFrom => string.IsNullOrWhiteSpace(FromAddress) ? Username : FromAddress;
}

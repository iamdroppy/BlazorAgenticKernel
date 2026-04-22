using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AgenticBlazorApp.Services;

/// <summary>
/// Scoped per Blazor circuit.  Holds a <see cref="ChatHistory"/> so the
/// conversation keeps context across turns, and routes every user turn
/// through the Kernel with auto tool calling.
///
/// Also subscribes to the singleton <see cref="AlertService"/> so alerts
/// fired by Hangfire background jobs show up in-line in the chat stream,
/// not just on the Home page.
/// </summary>
public sealed class ChatService : IDisposable
{
    private const string SystemPrompt = """
        You are an agentic assistant running in a single-page Blazor chat
        surface that renders your replies as Markdown.  Use Markdown freely
        — headings, **bold**, *italic*, lists, tables, links, and fenced
        ```code``` blocks are all fine.  Keep answers tight and scannable.

        You have four tool groups available:

        • News.ReadBbcNews — fetches current BBC News headlines.
          Call this whenever the user asks for BBC news right now.

        • Browser.ReadUrl — fetches and extracts the text of any http(s)
          URL.  Call this whenever the user asks you to read, summarise,
          fetch, or open a specific URL.  Pass the URL exactly as given.

        • Mail.* — multi-account IMAP/SMTP:
            - Mail.ListAccounts()                        — list configured accounts.
            - Mail.ReadInbox(account, count)             — read latest messages.
            - Mail.SearchInbox(account, query, count)    — search messages.
            - Mail.SendEmail(account, to, subject, body) — send via SMTP.
          If the user doesn't name an account, call Mail.ListAccounts first
          and ask them which one to use (unless there's only one).

        • Scheduler.* — Hangfire-backed one-off and recurring jobs:
            - Scheduler.ScheduleAlert(intent, delayMinutes)
                ONE-OFF. Fires once after delayMinutes.  Use for:
                "alert me in 5 minutes…", "remind me at …", "ping me in X".
            - Scheduler.ScheduleEveryMinutes(intent, minutes, jobId?)
                RECURRING shortcut. Use for "every N minutes/hours"
                (pass minutes=60 for "every hour").
            - Scheduler.ScheduleRecurring(intent, cron, jobId?)
                RECURRING with a 5-field cron (UTC). Use for calendar-style
                asks: "every day at 9am"  -> '0 9 * * *';
                "weekdays at 8"           -> '0 8 * * 1-5';
                "every 2 hours"           -> '0 */2 * * *'.
            - Scheduler.ListRecurring()            — list active recurring jobs.
            - Scheduler.RemoveRecurring(jobId)     — cancel a recurring job.
          jobId is optional; reuse the same id to overwrite an existing
          schedule.  Extract a clear 'intent' ("read BBC news", "email alice
          from work about my inbox", …) so the scheduled run can pick the
          right tools.  Alerts are injected into this chat when they fire.

        If the user just chats, chat back — tools are optional.
        """;

    private readonly Kernel _kernel;
    private readonly AlertService _alerts;
    private readonly ILogger<ChatService> _log;
    private readonly ChatHistory _history;
    private readonly object _sync = new();

    public event Action? Changed;

    public ChatService(Kernel kernel, AlertService alerts, ILogger<ChatService> log)
    {
        _kernel = kernel;
        _alerts = alerts;
        _log = log;
        _history = new ChatHistory();
        _history.AddSystemMessage(SystemPrompt);

        // Fired events from Hangfire arrive on a background thread —
        // the UI layer (Chat.razor) marshals back to the circuit via InvokeAsync.
        _alerts.Fired += OnAlertFired;
    }

    /// <summary>
    /// User + assistant messages only — the system prompt and internal
    /// tool-call plumbing are hidden from the UI.
    /// </summary>
    public IEnumerable<ChatMessageContent> VisibleMessages
    {
        get
        {
            lock (_sync)
            {
                return _history
                    .Where(m => (m.Role == AuthorRole.User || m.Role == AuthorRole.Assistant)
                                && !string.IsNullOrWhiteSpace(m.Content))
                    .ToList();
            }
        }
    }

    public bool IsEmpty => !VisibleMessages.Any();

    public async Task SendAsync(string userMessage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return;

        lock (_sync) _history.AddUserMessage(userMessage);
        Changed?.Invoke();

        var chat = _kernel.GetRequiredService<IChatCompletionService>();
        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            Temperature = 0.3
        };

        try
        {
            var reply = await chat.GetChatMessageContentAsync(_history, settings, _kernel, ct);
            lock (_sync)
            {
                if (!_history.Contains(reply))
                    _history.Add(reply);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Chat turn failed");
            lock (_sync) _history.AddAssistantMessage($"(error: {ex.Message})");
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _history.Clear();
            _history.AddSystemMessage(SystemPrompt);
        }
        Changed?.Invoke();
    }

    private void OnAlertFired(AlertNotification n)
    {
        try
        {
            var text = $"**{n.Title}**\n\n{n.Body}".Trim();
            lock (_sync) _history.AddAssistantMessage(text);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to append alert to chat");
        }
    }

    public void Dispose()
    {
        _alerts.Fired -= OnAlertFired;
    }
}

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AgenticBlazorApp.Services;

/// <summary>
/// Front door for user prompts coming from the Blazor UI.
/// Delegates to Semantic Kernel with auto tool calling so the LLM can
/// decide whether to schedule a Hangfire job or fetch content directly.
/// </summary>
public sealed class AgenticService
{
    private readonly Kernel _kernel;
    private readonly ILogger<AgenticService> _log;

    private const string SystemPrompt = """
        You are an agentic assistant rendered in a Markdown-aware chat UI.
        Markdown is welcome — headings, lists, tables, fenced code blocks,
        links.  You own four tool groups:

        • Scheduler.* — Hangfire-backed one-off and recurring jobs.
            - Scheduler.ScheduleAlert(intent, delayMinutes)
                ONE-OFF. "alert me in X", "remind me in Y minutes".
            - Scheduler.ScheduleEveryMinutes(intent, minutes, jobId?)
                RECURRING shortcut. "every 10 min", "every hour" (minutes=60).
            - Scheduler.ScheduleRecurring(intent, cron, jobId?)
                RECURRING cron (5-field, UTC). Examples:
                  '0 9 * * *'   (daily at 09:00),
                  '0 9 * * 1-5' (weekdays at 09:00),
                  '0 */2 * * *' (every 2 hours).
            - Scheduler.ListRecurring()        — list active recurring jobs.
            - Scheduler.RemoveRecurring(jobId) — cancel a recurring job.
          Extract a clean 'intent' describing what to do when the job fires
          (e.g. "read the BBC news", "summarise https://...", "email alice
          from work with my inbox"). Reuse a jobId to overwrite a schedule.

        • News.ReadBbcNews — current BBC News headlines.  Only call directly
          when the user wants news RIGHT NOW with no delay.

        • Browser.ReadUrl — fetches and extracts the text of any http(s)
          URL.  Call this whenever the user wants you to read, summarise,
          fetch, or open a specific URL RIGHT NOW.  Pass the URL exactly
          as given.

        • Mail.* — multi-account IMAP/SMTP:
            - Mail.ListAccounts()                        — list configured accounts.
            - Mail.ReadInbox(account, count)             — read latest messages.
            - Mail.SearchInbox(account, query, count)    — search messages.
            - Mail.SendEmail(account, to, subject, body) — send via SMTP.
          The 'account' is a short name; call Mail.ListAccounts if you're
          not sure which to use.

        Keep replies tight and scannable.
        """;

    public AgenticService(Kernel kernel, ILogger<AgenticService> log)
    {
        _kernel = kernel;
        _log = log;
    }

    public async Task<string> HandleAsync(string userPrompt, CancellationToken ct = default)
    {
        _log.LogInformation("User prompt: {Prompt}", userPrompt);

        var chat = _kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        history.AddSystemMessage(SystemPrompt);
        history.AddUserMessage(userPrompt);

        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            Temperature = 0.1
        };

        var reply = await chat.GetChatMessageContentAsync(history, settings, _kernel, ct);
        var content = reply.Content ?? "(no content)";
        _log.LogInformation("Agent reply: {Reply}", content);
        return content;
    }
}

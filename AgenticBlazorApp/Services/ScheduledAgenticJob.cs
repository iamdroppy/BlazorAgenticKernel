using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AgenticBlazorApp.Services;

/// <summary>
/// Hangfire entry point.  When a scheduled job fires, Hangfire resolves
/// this type from DI in a fresh scope and calls <see cref="RunAsync"/>.
///
/// RunAsync re-invokes the Semantic Kernel with ONLY the content plugins
/// loaded (Scheduler is removed to prevent re-scheduling loops) and
/// publishes the result to the AlertService bus so the Blazor UI can
/// surface it as a browser notification + chat message.
/// </summary>
public sealed class ScheduledAgenticJob
{
    private readonly Kernel _kernel;
    private readonly AlertService _alerts;
    private readonly ILogger<ScheduledAgenticJob> _log;

    private const string SystemPrompt = """
        You are an alert agent.  The user previously asked to be alerted
        about something and that moment has now arrived.  Use the available
        tools (e.g. News.ReadBbcNews, Browser.ReadUrl, Mail.*) to fetch
        fresh content and then produce a short, scannable Markdown summary
        suitable for a push notification (the UI renders Markdown).
        Keep it under ~120 words.
        """;

    public ScheduledAgenticJob(
        Kernel kernel,
        AlertService alerts,
        ILogger<ScheduledAgenticJob> log)
    {
        _kernel = kernel;
        _alerts = alerts;
        _log = log;
    }

    // Called by Hangfire. Must be public, returning Task.
    public async Task RunAsync(string intent, CancellationToken ct)
    {
        _log.LogInformation("Agentic job firing. Intent: {Intent}", intent);

        // Clone so the mutation below doesn't leak into future requests.
        var executionKernel = _kernel.Clone();
        if (executionKernel.Plugins.TryGetPlugin("Scheduler", out var schedulerPlugin))
        {
            executionKernel.Plugins.Remove(schedulerPlugin);
        }

        var chat = executionKernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage(SystemPrompt);
        history.AddUserMessage($"Fire the alert. Original intent: \"{intent}\".");

        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            Temperature = 1
        };

        string body;
        try
        {
            var reply = await chat.GetChatMessageContentAsync(history, settings, executionKernel, ct);
            body = reply.Content ?? "(no content)";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Agentic job failed while executing intent {Intent}", intent);
            body = $"Alert fired but the agent failed: {ex.Message}";
        }

        _alerts.Publish(new AlertNotification(
            Title: $"⏰ {intent}",
            Body: body,
            FiredAt: DateTimeOffset.Now));
    }
}

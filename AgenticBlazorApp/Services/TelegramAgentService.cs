using System.Collections.Concurrent;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace AgenticBlazorApp.Services;

/// <summary>
/// Long-polls Telegram and routes every user message through the same
/// Semantic Kernel (with auto tool calling) that powers the Blazor chat.
///
/// Mirrors <see cref="ChatService"/> behaviour:
///   • Per-Telegram-chat <see cref="ChatHistory"/> so multi-turn context
///     is preserved across messages.
///   • Auto-invoked tool calling against the full plugin catalogue loaded
///     into the DI <see cref="Kernel"/> (News, Browser, Mail, Scheduler,
///     WebSearch, BtcUsdCurrency — whatever is registered in Program.cs).
///   • Subscribes to <see cref="AlertService"/> so alerts raised by
///     Hangfire scheduled jobs fan out to every active Telegram chat, in
///     addition to the Blazor UI.
///
/// Config: set <c>Telegram:BotToken</c> (use user-secrets in dev).  If the
/// token is missing the service stays idle and the Blazor chat works as
/// normal.
/// </summary>
public sealed class TelegramAgentService : BackgroundService
{
    private const string SystemPrompt = """
        You are an agentic assistant replying over Telegram.  Keep answers
        tight and scannable — Telegram does not render rich Markdown, so
        write in plain prose with short paragraphs and, when helpful,
        dash-bulleted lists.  Avoid tables and heavy formatting.

        You have the same tool groups as the web agent:

        • News.ReadBbcNews — current BBC News headlines.  Call this when
          the user wants BBC news right now.

        • Browser.ReadUrl — fetch and extract the text of any http(s) URL.
          Pass the URL exactly as given.

        • WebSearch.* — DuckDuckGo-backed web search.

        • Mail.* — multi-account IMAP/SMTP:
            - Mail.ListAccounts()
            - Mail.ReadInbox(account, count)
            - Mail.SearchInbox(account, query, count)
            - Mail.SendEmail(account, to, subject, body)
          If the user doesn't name an account, call Mail.ListAccounts
          first and ask which one to use (unless there is only one).

        • Scheduler.* — Hangfire-backed one-off and recurring jobs:
            - Scheduler.ScheduleAlert(intent, delayMinutes)          ONE-OFF.
            - Scheduler.ScheduleEveryMinutes(intent, minutes, jobId?) RECURRING (minutes).
            - Scheduler.ScheduleEveryHours(intent, hours, jobId?)     RECURRING (hours).
            - Scheduler.ScheduleRecurring(intent, cron, jobId?)       RECURRING (5-field cron, UTC).
            - Scheduler.ListRecurring()
            - Scheduler.RemoveRecurring(jobId)
          Extract a clear 'intent' describing what to do on each tick.
          jobId is optional; reuse it to overwrite an existing schedule.
          When a scheduled alert fires, its result is pushed to this chat
          automatically — no need to poll.

        • BtcUsdCurrency.* — BTC/USD price lookups.

        If the user just chats, chat back — tools are optional.
        """;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertService _alerts;
    private readonly IConfiguration _cfg;
    private readonly ILogger<TelegramAgentService> _log;

    // One ChatHistory per Telegram chat id.
    private readonly ConcurrentDictionary<long, ChatHistory> _histories = new();

    // One-in-one-out gate per chat so turns never interleave.
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _chatLocks = new();

    // Chats we've seen this process-lifetime — used to fan scheduled alerts out.
    private readonly HashSet<long> _activeChats = new();
    private readonly object _activeSync = new();

    private ITelegramBotClient? _bot;

    public TelegramAgentService(
        IServiceScopeFactory scopeFactory,
        AlertService alerts,
        IConfiguration cfg,
        ILogger<TelegramAgentService> log)
    {
        _scopeFactory = scopeFactory;
        _alerts = alerts;
        _cfg = cfg;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = _cfg["Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            _log.LogWarning(
                "Telegram:BotToken is not configured — TelegramAgentService will stay idle. " +
                "Set it in appsettings.local.json or user-secrets to enable the Telegram bot.");
            return;
        }

        _bot = new TelegramBotClient(token);
        _alerts.Fired += OnAlertFired;

        try
        {
            var me = await _bot.GetMe(stoppingToken);
            _log.LogInformation(
                "Telegram agent online as @{Username} (id {Id}).", me.Username, me.Id);

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = new[] { UpdateType.Message },
                DropPendingUpdates = true
            };

            // ReceiveAsync blocks until cancellation.
            await _bot.ReceiveAsync(
                updateHandler: HandleUpdate,
                errorHandler: HandleError,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Telegram agent terminated unexpectedly.");
        }
        finally
        {
            _alerts.Fired -= OnAlertFired;
        }
    }

    // -------------------------------------------------------------------
    // Telegram update handling
    // -------------------------------------------------------------------

    private async Task HandleUpdate(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message is not { } msg) return;
        if (msg.Text is not { } text || string.IsNullOrWhiteSpace(text)) return;

        var chatId = msg.Chat.Id;
        lock (_activeSync) _activeChats.Add(chatId);

        var gate = _chatLocks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (text.StartsWith('/') && await TryHandleCommand(chatId, text, ct))
                return;

            try
            {
                await bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);
            }
            catch { /* best-effort */ }

            string reply;
            try
            {
                reply = await RunTurnAsync(chatId, text, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Telegram turn failed for chat {Chat}", chatId);
                reply = $"(error: {ex.Message})";
            }

            await SendAsync(chatId, reply, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<bool> TryHandleCommand(long chatId, string text, CancellationToken ct)
    {
        // Strip trailing @botname from commands (group chats).
        var cmd = text.Split(' ', 2)[0];
        cmd = cmd.Split('@')[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/start":
                await SendAsync(chatId,
                    "Hi — I'm the Agentic Kernel bot.\n\n" +
                    "Ask me anything: I can read the web, work your inbox, and " +
                    "schedule one-off or recurring jobs that ping you back here.\n\n" +
                    "/help for commands.", ct);
                return true;

            case "/reset":
            case "/clear":
                _histories.TryRemove(chatId, out _);
                await SendAsync(chatId, "Conversation cleared.", ct);
                return true;

            case "/help":
                await SendAsync(chatId,
                    "Commands:\n" +
                    "/start — greeting\n" +
                    "/reset — clear this chat's memory\n" +
                    "/help — this message\n\n" +
                    "Otherwise just type and I'll use tools automatically.", ct);
                return true;
        }

        return false;
    }

    private async Task<string> RunTurnAsync(long chatId, string userMessage, CancellationToken ct)
    {
        var history = _histories.GetOrAdd(chatId, _ =>
        {
            var h = new ChatHistory();
            h.AddSystemMessage(SystemPrompt);
            return h;
        });

        // Fresh DI scope per turn — Kernel + plugins are registered scoped.
        using var scope = _scopeFactory.CreateScope();
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        lock (history) history.AddUserMessage(userMessage);

        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            Temperature = 1
        };

        var reply = await chat.GetChatMessageContentAsync(history, settings, kernel, ct);

        lock (history)
        {
            if (!history.Contains(reply))
                history.Add(reply);
        }

        return reply.Content ?? "(no content)";
    }

    private Task HandleError(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        if (ex is ApiRequestException api)
            _log.LogError(api, "Telegram API error {Code}: {Message}", api.ErrorCode, api.Message);
        else
            _log.LogError(ex, "Telegram polling error");
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------
    // AlertService fan-out — scheduled jobs push results to every chat
    // that has ever talked to the bot during this process lifetime.
    // -------------------------------------------------------------------

    private void OnAlertFired(AlertNotification n)
    {
        if (_bot is null) return;

        long[] targets;
        lock (_activeSync) targets = _activeChats.ToArray();
        if (targets.Length == 0) return;

        var text = $"⏰ {n.Title}\n\n{n.Body}".Trim();

        // Fire-and-forget — don't block the AlertService event thread.
        _ = Task.Run(async () =>
        {
            foreach (var chatId in targets)
            {
                try { await SendAsync(chatId, text, CancellationToken.None); }
                catch (Exception ex)
                {
                    _log.LogWarning(ex,
                        "Failed to push alert to Telegram chat {Chat}", chatId);
                }
            }
        });
    }

    // -------------------------------------------------------------------
    // Sending — chunked to stay under Telegram's 4096-char message limit.
    // -------------------------------------------------------------------

    private async Task SendAsync(long chatId, string text, CancellationToken ct)
    {
        if (_bot is null) return;
        if (string.IsNullOrWhiteSpace(text)) text = "(no content)";

        const int MaxLen = 4000;
        int i = 0;
        while (i < text.Length)
        {
            int remaining = text.Length - i;
            int take = Math.Min(MaxLen, remaining);

            // If we're cutting mid-stream, try to break on a newline or space.
            if (take < remaining)
            {
                var slice = text.AsSpan(i, take);
                int cut = slice.LastIndexOf('\n');
                if (cut < MaxLen / 2) cut = slice.LastIndexOf(' ');
                if (cut > 0) take = cut;
            }

            var chunk = text.Substring(i, take);
            try
            {
                await _bot.SendMessage(chatId, chunk, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Telegram SendMessage failed for chat {Chat}", chatId);
                return;
            }

            i += take;
            // Skip the whitespace we used as the split point.
            while (i < text.Length && (text[i] == ' ' || text[i] == '\n')) i++;
        }
    }
}

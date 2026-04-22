using System.ComponentModel;
using System.Text;
using AgenticBlazorApp.Services;
using Hangfire;
using Hangfire.Storage;
using Microsoft.SemanticKernel;

namespace AgenticBlazorApp.Plugins;

/// <summary>
/// Semantic Kernel plugin that schedules agentic runs via Hangfire.
///
/// Two flavours:
///   • One-off    — <see cref="ScheduleAlert"/>  (fires once after a delay).
///   • Recurring  — <see cref="ScheduleRecurring"/> + <see cref="ScheduleEveryMinutes"/>
///                  (keep firing on a cron until removed).
///
/// Recurring jobs persist across process restarts because Hangfire stores
/// them in SQLite.  Hangfire also passes its own cancellation token into
/// <see cref="ScheduledAgenticJob.RunAsync"/> on every tick.
/// </summary>
public sealed class SchedulerPlugin
{
    private readonly IBackgroundJobClient _jobs;
    private readonly IRecurringJobManager _recurring;
    private readonly ILogger<SchedulerPlugin> _log;

    public SchedulerPlugin(
        IBackgroundJobClient jobs,
        IRecurringJobManager recurring,
        ILogger<SchedulerPlugin> log)
    {
        _jobs = jobs;
        _recurring = recurring;
        _log = log;
    }

    // =====================================================================
    // One-off schedule (existing behaviour)
    // =====================================================================

    [KernelFunction("ScheduleAlert")]
    [Description(
        "Schedule a ONE-OFF future alert. When the delay elapses the agent will be " +
        "re-invoked with the given intent so it can pick the appropriate tool " +
        "(e.g. ReadBbcNews, ReadUrl, SendEmail) and deliver the result as a notification.")]
    public string ScheduleAlert(
        [Description("Short natural-language description of what to do when the alert fires, e.g. 'read the BBC news'.")]
        string intent,
        [Description("Delay in minutes from now. Use 0 for immediate.")]
        int delayMinutes)
    {
        var delay = TimeSpan.FromMinutes(Math.Max(0, delayMinutes));

        var jobId = _jobs.Schedule<ScheduledAgenticJob>(
            job => job.RunAsync(intent, CancellationToken.None),
            delay);

        _log.LogInformation("Scheduled one-off job {JobId} in {Delay} for intent '{Intent}'",
            jobId, delay, intent);

        return $"Scheduled alert (job #{jobId}) to fire in {delayMinutes} minute(s). " +
               $"Intent: \"{intent}\".";
    }

    // =====================================================================
    // Recurring schedule — cron
    // =====================================================================

    [KernelFunction("ScheduleRecurring")]
    [Description(
        "Schedule a RECURRING agentic job using a standard 5-field cron expression " +
        "(minute hour day month day-of-week, UTC). Examples: '*/10 * * * *' fires " +
        "every 10 minutes; '0 9 * * *' fires at 09:00 daily; '0 9 * * 1-5' fires at " +
        "09:00 on weekdays; '0 */2 * * *' fires every 2 hours. The job will run until " +
        "you call RemoveRecurring. If jobId is empty a new id is generated; passing " +
        "an existing jobId REPLACES that schedule.")]
    public string ScheduleRecurring(
        [Description("Short natural-language description of what the agent should do on each tick.")]
        string intent,
        [Description("5-field cron expression (minute hour day-of-month month day-of-week, UTC).")]
        string cron,
        [Description("Optional stable id. Reuse the same id to update an existing schedule. Leave empty for auto.")]
        string? jobId = null)
    {
        if (string.IsNullOrWhiteSpace(intent))
            return "Error: 'intent' is required.";
        if (string.IsNullOrWhiteSpace(cron))
            return "Error: 'cron' is required.";

        var id = NormaliseJobId(jobId);

        try
        {
            _recurring.AddOrUpdate<ScheduledAgenticJob>(
                id,
                job => job.RunAsync(intent, CancellationToken.None),
                cron.Trim());
        }
        catch (Exception ex)
        {
            return $"Error: invalid cron '{cron}' — {ex.Message}";
        }

        _log.LogInformation("Scheduled recurring job {JobId} cron='{Cron}' intent='{Intent}'",
            id, cron, intent);

        return $"Recurring job '{id}' scheduled on cron '{cron}'. Intent: \"{intent}\".";
    }

    // =====================================================================
    // Recurring schedule — simple N-minute interval (LLM-friendly shortcut)
    // =====================================================================

    [KernelFunction("ScheduleEveryMinutes")]
    [Description(
        "Shortcut to schedule a RECURRING job that fires every N minutes (1..1440). " +
        "Use this for requests like 'every 10 minutes', 'every hour' (minutes=60), " +
        "'every 15 min'. For calendar-style schedules ('9am on weekdays') use " +
        "ScheduleRecurring with a cron expression instead.")]
    public string ScheduleEveryMinutes(
        [Description("Short natural-language description of what the agent should do on each tick.")]
        string intent,
        [Description("Interval in minutes, between 1 and 1440.")]
        int minutes,
        [Description("Optional stable id. Reuse to update; leave empty for auto.")]
        string? jobId = null)
    {
        if (minutes < 1) minutes = 1;
        if (minutes > 1440) minutes = 1440;

        string cron;
        if (minutes == 1)
            cron = "* * * * *";
        else if (minutes < 60)
            cron = $"*/{minutes} * * * *";
        else if (minutes % 60 == 0 && minutes <= 720)
            cron = $"0 */{minutes / 60} * * *";
        else
            cron = $"*/{minutes} * * * *"; // best-effort fallback

        return ScheduleRecurring(intent, cron, jobId);
    }

    [KernelFunction("ScheduleEveryHours")]
    [Description(
        "Shortcut to schedule a RECURRING job that fires every N hours (1..24). " +
        "Use this for requests like 'every hour', 'every 2 hours'. For minute-level " +
        "schedules ('every 10 minutes') use ScheduleEveryMinutes; for calendar-style " +
        "schedules ('9am on weekdays') use ScheduleRecurring with a cron expression.")]
    public string ScheduleEveryHours(
        [Description("Short natural-language description of what the agent should do on each tick.")]
        string intent,
        [Description("Interval in hours, between 1 and 24.")]
        int hours,
        [Description("Optional stable id. Reuse to update; leave empty for auto.")]
        string? jobId = null)
    {
        if (hours < 1) hours = 1;
        if (hours > 24) hours = 24;
        string cron;
        if (hours == 1)
            cron = "0 * * * *";
        else
            cron = $"0 */{hours} * * *";
        return ScheduleRecurring(intent, cron, jobId);
    }

    // =====================================================================
    // Recurring schedule — list
    // =====================================================================

    [KernelFunction("ListRecurring")]
    [Description(
        "List currently registered recurring agentic jobs (id, cron, last run, next run, intent).")]
    public string ListRecurring()
    {
        using var conn = JobStorage.Current.GetConnection();
        var jobs = conn.GetRecurringJobs();
        if (jobs == null || jobs.Count == 0)
            return "No recurring jobs are currently scheduled.";

        var sb = new StringBuilder();
        sb.Append(jobs.Count).AppendLine(" recurring job(s):");
        foreach (var j in jobs.OrderBy(j => j.Id))
        {
            sb.Append("- ").Append(j.Id)
              .Append("  cron='").Append(j.Cron).Append('\'');
            if (j.NextExecution is { } next)
                sb.Append("  next=").Append(next.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            if (j.LastExecution is { } last)
                sb.Append("  last=").Append(last.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));

            var intent = ExtractIntent(j);
            if (!string.IsNullOrWhiteSpace(intent))
                sb.Append("  intent=\"").Append(intent).Append('"');

            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    // =====================================================================
    // Recurring schedule — remove
    // =====================================================================

    [KernelFunction("RemoveRecurring")]
    [Description(
        "Remove (cancel) a recurring agentic job by id. No-op if the id doesn't exist.")]
    public string RemoveRecurring(
        [Description("Exact recurring-job id. Use ListRecurring to discover valid ids.")]
        string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return "Error: 'jobId' is required.";

        _recurring.RemoveIfExists(jobId.Trim());
        _log.LogInformation("Removed recurring job {JobId}", jobId);
        return $"Removed recurring job '{jobId}' (if it existed).";
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static string NormaliseJobId(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return "agent-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "-" + DateTime.Now.ToString("ddMMyyyy-hhmmss");

        // Hangfire job ids should be URL/path-safe.
        var trimmed = requested.Trim();
        var safe = new string(trimmed.Select(c =>
            char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "agent-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "-" + DateTime.Now.ToString("ddMMyyyy-hhmmss")
                                               : safe;
    }

    /// <summary>
    /// Pull the first argument (the stored intent string) out of a recurring
    /// job descriptor so ListRecurring can show it.  Uses the public
    /// <c>RecurringJobDto.Job</c>.Args property when available.
    /// </summary>
    private static string ExtractIntent(RecurringJobDto dto)
    {
        try
        {
            var args = dto.Job?.Args;
            if (args is { Count: > 0 } && args[0] is string s) return s;
        }
        catch
        {
            // Best-effort only.
        }
        return "";
    }
}

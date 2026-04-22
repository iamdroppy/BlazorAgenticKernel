namespace AgenticBlazorApp.Services;

/// <summary>
/// A single alert that fired from a scheduled job.
/// </summary>
public record AlertNotification(string Title, string Body, DateTimeOffset FiredAt);

/// <summary>
/// Singleton pub/sub bus that lets Hangfire background jobs push notifications
/// to any currently-connected Blazor circuit.
/// </summary>
public sealed class AlertService
{
    private readonly List<AlertNotification> _history = new();
    private readonly object _sync = new();

    /// <summary>Fires whenever a new alert is published.</summary>
    public event Action<AlertNotification>? Fired;

    public IReadOnlyList<AlertNotification> History
    {
        get
        {
            lock (_sync) return _history.ToArray();
        }
    }

    public void Publish(AlertNotification notification)
    {
        lock (_sync) _history.Add(notification);
        Fired?.Invoke(notification);
    }
}

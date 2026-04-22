using System.Collections.Immutable;

namespace MailPlugin;

/// <summary>
/// Singleton holder of all configured mail accounts.  Populated from
/// <c>Mail:Accounts</c> at startup; the agent looks up accounts by
/// <see cref="MailAccountOptions.Name"/> (case-insensitive).
/// </summary>
public sealed class MailAccountRegistry
{
    private readonly ImmutableDictionary<string, MailAccountOptions> _byName;

    public MailAccountRegistry(IEnumerable<MailAccountOptions> accounts)
    {
        var valid = (accounts ?? Array.Empty<MailAccountOptions>())
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .ToList();

        _byName = valid.ToImmutableDictionary(
            a => a.Name.Trim(),
            a => a,
            StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<MailAccountOptions> All => _byName.Values;

    public bool HasAny => _byName.Count > 0;

    public MailAccountOptions? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _byName.TryGetValue(name.Trim(), out var a) ? a : null;
    }
}

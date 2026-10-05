using System.Text.RegularExpressions;

namespace restByAno.Core.Resolution;

public static partial class VariableNames
{
    /// <summary>Variable names are case-sensitive identifiers: [A-Za-z_][A-Za-z0-9_]*.</summary>
    public static bool IsValid(string? name) => name is not null && NamePattern().IsMatch(name);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex NamePattern();
}

public enum VariableStatus
{
    Available,

    /// <summary>The variable exists but its value cannot be produced (for example a secret that is missing or cannot be decrypted).</summary>
    Unreadable,
}

/// <summary>One variable at the moment a request or run starts. Values are memory-only.</summary>
public sealed record VariableEntry(string Name, string? Value, bool IsSecret, VariableStatus Status = VariableStatus.Available);

/// <summary>
/// Variables frozen at Send/Run time with precedence already applied (run &gt; environment &gt; collection defaults).
/// Later edits of environments or collections cannot change an execution that already has its snapshot.
/// </summary>
public sealed class VariableSnapshot
{
    public static VariableSnapshot Empty { get; } = new(new Dictionary<string, VariableEntry>(StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<string, VariableEntry> _entries;

    private VariableSnapshot(IReadOnlyDictionary<string, VariableEntry> entries) => _entries = entries;

    public bool TryGet(string name, out VariableEntry entry) => _entries.TryGetValue(name, out entry!);

    public IEnumerable<string> Names => _entries.Keys;

    /// <summary>Values of secret variables, used to build the redaction context. Never persisted or logged.</summary>
    public IReadOnlyList<string> SecretValues =>
        _entries.Values.Where(e => e.IsSecret && !string.IsNullOrEmpty(e.Value)).Select(e => e.Value!).Distinct().ToList();

    /// <summary>Starts a builder; add layers from lowest to highest precedence.</summary>
    public static Builder CreateBuilder() => new();

    public sealed class Builder
    {
        private readonly Dictionary<string, VariableEntry> _entries = new(StringComparer.Ordinal);

        /// <summary>Adds a layer; its entries replace same-named entries from lower layers.</summary>
        public Builder AddLayer(IEnumerable<VariableEntry> layer)
        {
            foreach (var entry in layer)
                _entries[entry.Name] = entry;
            return this;
        }

        public Builder AddValue(string name, string value, bool isSecret = false)
        {
            _entries[name] = new VariableEntry(name, value, isSecret);
            return this;
        }

        public VariableSnapshot Build() => new(new Dictionary<string, VariableEntry>(_entries, StringComparer.Ordinal));
    }
}

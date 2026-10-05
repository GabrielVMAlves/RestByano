namespace restByAno.Core.Resolution;

/// <summary>
/// Known secret values for one request or run. Diagnostics, progress text and reports pass text through
/// <see cref="Redact"/> so a credential can never be echoed, and only allowlisted fields are written in the first place.
/// </summary>
public sealed class RedactionContext
{
    public const string Mask = "[redacted]";

    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "X-API-Key", "X-Auth-Token", "Api-Key",
    };

    private readonly List<string> _secrets = [];

    public static RedactionContext Empty => new();

    public void Add(IEnumerable<string> secrets)
    {
        foreach (var secret in secrets)
            Add(secret);
    }

    public void Add(string? secret)
    {
        if (string.IsNullOrEmpty(secret) || _secrets.Contains(secret, StringComparer.Ordinal)) return;
        _secrets.Add(secret);
        _secrets.Sort((a, b) => b.Length.CompareTo(a.Length)); // replace longer secrets first
    }

    /// <summary>Replaces every known secret value (as plain text, Base64 or percent-encoded) with a mask.</summary>
    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var result = text;
        foreach (var secret in _secrets)
        {
            result = result.Replace(secret, Mask, StringComparison.Ordinal);
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(secret));
            result = result.Replace(encoded, Mask, StringComparison.Ordinal);
            var escaped = Uri.EscapeDataString(secret);
            if (escaped != secret)
                result = result.Replace(escaped, Mask, StringComparison.Ordinal);
        }

        return result;
    }

    public static bool IsSensitiveHeader(string name) => SensitiveHeaderNames.Contains(name);
}

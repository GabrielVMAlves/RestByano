using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace restByAno.Core.Resolution;

/// <summary>Outcome of resolving one template. Problems list variable names only — never values.</summary>
public sealed class ResolutionProblems
{
    private readonly SortedSet<string> _missing = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _unreadable = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _invalid = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Missing => _missing;
    public IReadOnlyCollection<string> Unreadable => _unreadable;
    public IReadOnlyCollection<string> InvalidReferences => _invalid;

    public bool Any => _missing.Count + _unreadable.Count + _invalid.Count > 0;

    internal void AddMissing(string name) => _missing.Add(name);
    internal void AddUnreadable(string name) => _unreadable.Add(name);
    internal void AddInvalid(string text) => _invalid.Add(text);

    public IEnumerable<string> ToMessages()
    {
        if (_missing.Count > 0)
            yield return $"Undefined variable(s): {string.Join(", ", _missing)}. Define them in the selected environment or the collection variables.";
        if (_unreadable.Count > 0)
            yield return $"Variable(s) that cannot be read: {string.Join(", ", _unreadable)}. A secret may be missing or no longer decryptable on this account; enter it again.";
        if (_invalid.Count > 0)
            yield return $"Invalid variable reference(s): {string.Join(", ", _invalid.Select(i => "{{" + i + "}}"))}. Names use letters, digits and underscores and cannot start with a digit.";
    }
}

/// <summary>
/// Substitutes <c>{{name}}</c> references. Resolution is a single, non-recursive pass: text that comes out of a
/// variable is never scanned for further references.
/// </summary>
public static partial class TemplateResolver
{
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    [GeneratedRegex(@"\{\{([^{}]*)\}\}")]
    private static partial Regex ReferencePattern();

    public static string ResolveText(string template, VariableSnapshot variables, ResolutionProblems problems)
    {
        if (template.Length == 0 || !template.Contains("{{", StringComparison.Ordinal))
            return template;

        return ReferencePattern().Replace(template, match =>
        {
            var name = match.Groups[1].Value.Trim();
            if (!VariableNames.IsValid(name))
            {
                problems.AddInvalid(match.Groups[1].Value.Trim());
                return match.Value;
            }

            if (!variables.TryGet(name, out var entry))
            {
                problems.AddMissing(name);
                return match.Value;
            }

            if (entry.Status == VariableStatus.Unreadable || entry.Value is null)
            {
                problems.AddUnreadable(name);
                return match.Value;
            }

            return entry.Value;
        });
    }

    /// <summary>
    /// Parses the template as JSON first, then substitutes only inside string VALUES and serializes the result with
    /// System.Text.Json (so quotes, backslashes and newlines in a value are escaped exactly once).
    /// Property names and non-string values are never substituted; an unquoted <c>{{value}}</c> is rejected.
    /// Returns null (with an explanatory error) when the template is not valid JSON.
    /// </summary>
    public static string? ResolveJson(string template, VariableSnapshot variables, ResolutionProblems problems, out string? error)
    {
        error = null;
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(template, nodeOptions: null, documentOptions: new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            var position = ex is JsonException { LineNumber: { } line, BytePositionInLine: { } pos }
                ? $" (line {line + 1}, position {pos + 1})"
                : string.Empty;
            error = template.Contains("{{", StringComparison.Ordinal)
                ? $"The JSON body is not valid{position}. Variable placeholders must be inside quoted strings, for example {{\"id\":\"{{{{customerId}}}}\"}}; an unquoted {{{{value}}}} is not accepted."
                : $"The JSON body is not valid{position}.";
            return null;
        }

        if (root is null)
        {
            error = "The JSON body is empty.";
            return null;
        }

        var replaced = Walk(root, variables, problems);
        return replaced.ToJsonString(JsonWriteOptions);
    }

    private static JsonNode Walk(JsonNode node, VariableSnapshot variables, ResolutionProblems problems)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                    if (obj[key] is { } child && Walk(child, variables, problems) is { } walked && !ReferenceEquals(walked, child))
                        obj[key] = walked; // only leaf strings are replaced; containers are updated in place
                return obj;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    if (array[i] is { } item && Walk(item, variables, problems) is { } walked && !ReferenceEquals(walked, item))
                        array[i] = walked;
                return array;

            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                return JsonValue.Create(ResolveText(value.GetValue<string>(), variables, problems))!;

            default:
                return node;
        }
    }
}

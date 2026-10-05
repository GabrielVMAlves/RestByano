using System.Text.Json;
using restByAno.Core.Assertions;
using restByAno.Core.Models;

namespace restByAno.Core.Running;

/// <summary>Outcome of the extractors of one step. When any extractor fails, <see cref="Values"/> is empty (all or nothing).</summary>
public sealed record ExtractionResult(bool Succeeded, IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> Failures)
{
    public static ExtractionResult Empty { get; } = new(true, new Dictionary<string, string>(), []);
}

/// <summary>
/// Reads scalar values out of a JSON response. A string becomes its raw text; a number, boolean or null becomes its JSON
/// literal text; objects and lists are rejected. The values are applied atomically: if any extractor fails, none of this
/// step's variables are produced. Failure messages name pointers and variables, never values.
/// </summary>
public static class ResponseExtractor
{
    public static ExtractionResult Extract(IReadOnlyList<ExtractorDefinition> extractors, ResponseBodyJson body)
    {
        if (extractors.Count == 0)
            return ExtractionResult.Empty;

        var failures = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!body.TryGetRoot(out var root, out var bodyFailure))
            return new ExtractionResult(false, new Dictionary<string, string>(), [bodyFailure]);

        var number = 0;
        foreach (var extractor in extractors)
        {
            number++;
            var label = $"Extractor {number} (“{extractor.VariableName}” from “{(string.IsNullOrEmpty(extractor.JsonPointer) ? "(whole body)" : extractor.JsonPointer)}”)";

            if (!JsonPointerNavigator.TryParse(extractor.JsonPointer, out var tokens, out var pointerError))
            {
                failures.Add($"{label}: {pointerError}");
                continue;
            }

            if (!JsonPointerNavigator.TryResolve(root, tokens, out var element))
            {
                failures.Add($"{label}: the path was not found in the response.");
                continue;
            }

            string text;
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    text = element.GetString()!;
                    break;
                case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null:
                    text = element.GetRawText();
                    break;
                default:
                    failures.Add($"{label}: the value is an object or list; only scalar values can be extracted.");
                    continue;
            }

            if (!values.TryAdd(extractor.VariableName, text))
                failures.Add($"{label}: the variable is extracted more than once in this step.");
        }

        return failures.Count == 0
            ? new ExtractionResult(true, values, [])
            : new ExtractionResult(false, new Dictionary<string, string>(), failures);
    }
}

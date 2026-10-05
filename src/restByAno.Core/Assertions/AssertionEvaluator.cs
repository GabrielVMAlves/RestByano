using System.Globalization;
using System.Text.Json;
using restByAno.Core.Models;
using restByAno.Core.Resolution;

namespace restByAno.Core.Assertions;

/// <summary>
/// One evaluated assertion. <see cref="Result"/> is the persistable, value-free record; <see cref="Detail"/> is optional
/// text for the on-screen view only (expected/actual) and has already passed through the redaction context.
/// </summary>
public sealed record AssertionEvaluation(AssertionResult Result, string? Detail);

public static class AssertionEvaluator
{
    private const int MaxDetailLength = 80;

    public static IReadOnlyList<AssertionEvaluation> Evaluate(
        IReadOnlyList<AssertionDefinition> assertions, ResponseSnapshot response, ResponseBodyJson body, RedactionContext redaction)
    {
        var results = new List<AssertionEvaluation>(assertions.Count);
        foreach (var assertion in assertions)
            results.Add(EvaluateOne(assertion, response, body, redaction));
        return results;
    }

    private static AssertionEvaluation EvaluateOne(AssertionDefinition a, ResponseSnapshot response, ResponseBodyJson body, RedactionContext redaction)
    {
        AssertionEvaluation Make(bool passed, string reason, string? detail = null) => new(
            new AssertionResult
            {
                AssertionId = a.Id,
                Kind = a.Kind,
                JsonPointer = a.JsonPointer,
                HeaderName = a.HeaderName,
                Passed = passed,
                Reason = reason,
            },
            detail is null ? null : Shorten(redaction.Redact(detail)));

        switch (a.Kind)
        {
            case AssertionKind.StatusEquals:
                if (a.ExpectedStatus is not { } expectedStatus)
                    return Make(false, "The assertion has no expected status code.");
                return response.StatusCode == expectedStatus
                    ? Make(true, "Status code matched.", $"status {response.StatusCode}")
                    : Make(false, "Status code did not match.", $"expected {expectedStatus}, got {response.StatusCode}");

            case AssertionKind.HeaderEquals:
                return EvaluateHeader(a, response, Make);

            case AssertionKind.DurationBelow:
                if (a.MaxMilliseconds is not { } limit)
                    return Make(false, "The assertion has no time limit.");
                // Strict: exactly the limit fails.
                return response.ElapsedMilliseconds < limit
                    ? Make(true, "Response time was below the limit.", $"{response.ElapsedMilliseconds} ms < {limit} ms")
                    : Make(false, "Response time was not below the limit.", $"{response.ElapsedMilliseconds} ms is not < {limit} ms");

            case AssertionKind.JsonExists:
            case AssertionKind.JsonEquals:
                return EvaluateJson(a, body, redaction, Make);

            default:
                return Make(false, "The assertion type is not supported.");
        }
    }

    private static AssertionEvaluation EvaluateHeader(
        AssertionDefinition a, ResponseSnapshot response, Func<bool, string, string?, AssertionEvaluation> make)
    {
        if (string.IsNullOrWhiteSpace(a.HeaderName))
            return make(false, "The assertion has no header name.", null);
        if (!TryReadJsonString(a.ExpectedJson, out var expected))
            return make(false, "The expected header value is not a valid JSON string.", null);

        var values = response.Headers.Where(h => h.Name.Equals(a.HeaderName.Trim(), StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).ToList();
        if (values.Count == 0)
            return make(false, $"Header “{a.HeaderName}” was missing.", null);

        // Case-insensitive NAME, ordinal VALUE, against any single returned value; values are never split on commas.
        return values.Any(v => string.Equals(v, expected, StringComparison.Ordinal))
            ? make(true, $"Header “{a.HeaderName}” had the expected value.", null)
            : make(false, $"Header “{a.HeaderName}” did not have the expected value.", $"expected “{expected}”, got {string.Join(" | ", values.Select(v => $"“{v}”"))}");
    }

    private static AssertionEvaluation EvaluateJson(
        AssertionDefinition a, ResponseBodyJson body, RedactionContext redaction, Func<bool, string, string?, AssertionEvaluation> make)
    {
        if (!JsonPointerNavigator.TryParse(a.JsonPointer, out var tokens, out var pointerError))
            return make(false, pointerError ?? "The JSON pointer is not valid.", null);
        var path = string.IsNullOrEmpty(a.JsonPointer) ? "(whole body)" : a.JsonPointer;

        if (!body.TryGetRoot(out var root, out var failure))
            return make(false, failure, null);

        if (!JsonPointerNavigator.TryResolve(root, tokens, out var actual))
            return make(false, $"Path “{path}” was not found.", null);

        if (a.Kind == AssertionKind.JsonExists)
            return make(true, $"Path “{path}” exists.", null); // includes a JSON null value

        if (a.ExpectedJson is null)
            return make(false, "The assertion has no expected value.", null);
        JsonElement expected;
        try
        {
            using var doc = JsonDocument.Parse(a.ExpectedJson);
            expected = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return make(false, "The expected value is not valid JSON.", null);
        }

        if (expected.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            return make(false, "Only scalar values can be compared.", null);
        if (actual.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            return make(false, $"The value at “{path}” is an object or list; only scalars can be compared.", null);

        if (actual.ValueKind != expected.ValueKind && !(IsBool(actual) && IsBool(expected)))
            return make(false, $"The value at “{path}” has a different type.", $"expected {Describe(expected)}, got {Describe(actual)}");

        return ScalarEquals(actual, expected)
            ? make(true, $"The value at “{path}” matched.", $"{Describe(actual)}")
            : make(false, $"The value at “{path}” did not match.", $"expected {Describe(expected)}, got {Describe(actual)}");
    }

    private static bool IsBool(JsonElement e) => e.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool ScalarEquals(JsonElement actual, JsonElement expected) => actual.ValueKind switch
    {
        JsonValueKind.String => string.Equals(actual.GetString(), expected.GetString(), StringComparison.Ordinal),
        JsonValueKind.True or JsonValueKind.False => actual.GetBoolean() == expected.GetBoolean(),
        JsonValueKind.Null => true,
        JsonValueKind.Number => NumberEquals(actual, expected),
        _ => false,
    };

    /// <summary>Numbers compare as decimals when both fit; otherwise as finite doubles.</summary>
    private static bool NumberEquals(JsonElement actual, JsonElement expected)
    {
        if (actual.TryGetDecimal(out var a) && expected.TryGetDecimal(out var e))
            return a == e;
        if (actual.TryGetDouble(out var da) && expected.TryGetDouble(out var de) && double.IsFinite(da) && double.IsFinite(de))
            return da == de;
        return false;
    }

    private static string Describe(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => $"string “{e.GetString()}”",
        JsonValueKind.Number => $"number {e.GetRawText()}",
        JsonValueKind.True or JsonValueKind.False => $"boolean {e.GetRawText()}",
        JsonValueKind.Null => "null",
        _ => e.ValueKind.ToString(),
    };

    private static bool TryReadJsonString(string? json, out string value)
    {
        value = string.Empty;
        if (json is null) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.String) return false;
            value = doc.RootElement.GetString()!;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Shorten(string text) =>
        text.Length <= MaxDetailLength ? text : text[..MaxDetailLength] + "…";
}

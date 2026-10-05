using System.Text.Json;
using restByAno.Core.Models;
using restByAno.Core.Resolution;

namespace restByAno.Core.Assertions;

/// <summary>Field-level validation shared by the rule editors and any other code that builds rules.</summary>
public static class RuleValidator
{
    public static string? ValidateAssertion(AssertionDefinition a)
    {
        switch (a.Kind)
        {
            case AssertionKind.StatusEquals:
                return a.ExpectedStatus is >= 100 and <= 599 ? null : "Enter a status code from 100 to 599.";

            case AssertionKind.HeaderEquals:
                if (string.IsNullOrWhiteSpace(a.HeaderName)) return "Enter the header name.";
                if (a.HeaderName.Any(c => c <= ' ' || c == ':' || c > '~')) return "The header name contains invalid characters.";
                return IsJsonScalar(a.ExpectedJson, stringOnly: true) ? null : "Enter the expected header value.";

            case AssertionKind.JsonExists:
                return PointerError(a.JsonPointer);

            case AssertionKind.JsonEquals:
                var pointerError = PointerError(a.JsonPointer);
                if (pointerError is not null) return pointerError;
                return IsJsonScalar(a.ExpectedJson, stringOnly: false)
                    ? null
                    : "The expected value must be a JSON string (in quotes, e.g. \"Ada\"), a number, true, false or null. Objects and lists are not supported.";

            case AssertionKind.DurationBelow:
                return a.MaxMilliseconds is >= 1 ? null : "Enter a positive number of milliseconds.";

            default:
                return "The assertion type is not supported.";
        }
    }

    public static string? ValidateExtractor(ExtractorDefinition x)
    {
        var pointerError = PointerError(x.JsonPointer);
        if (pointerError is not null) return pointerError;
        return VariableNames.IsValid(x.VariableName)
            ? null
            : "The variable name must use letters, digits and underscores and cannot start with a digit.";
    }

    private static string? PointerError(string? pointer) =>
        JsonPointerNavigator.TryParse(pointer, out _, out var error) ? null : error;

    public static bool IsJsonScalar(string? json, bool stringOnly)
    {
        if (json is null) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var kind = doc.RootElement.ValueKind;
            return stringOnly
                ? kind == JsonValueKind.String
                : kind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

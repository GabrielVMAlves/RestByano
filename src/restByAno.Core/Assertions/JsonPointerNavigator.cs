using System.Text.Json;

namespace restByAno.Core.Assertions;

/// <summary>
/// RFC 6901 JSON Pointer. The empty pointer is the root; <c>/</c> separates tokens; <c>~1</c> means <c>/</c> and
/// <c>~0</c> means <c>~</c>; any other escape is invalid. Array indices are non-negative base-10 integers without leading
/// zeros (except <c>0</c>); <c>-</c> never identifies an existing value.
/// </summary>
public static class JsonPointerNavigator
{
    public static bool IsValid(string? pointer) => TryParse(pointer, out _, out _);

    public static bool TryParse(string? pointer, out IReadOnlyList<string> tokens, out string? error)
    {
        tokens = [];
        error = null;
        if (pointer is null)
        {
            error = "The JSON pointer is missing.";
            return false;
        }

        if (pointer.Length == 0)
            return true; // the whole document

        if (pointer[0] != '/')
        {
            error = "A JSON pointer must be empty (the whole body) or start with “/”, for example /data/id.";
            return false;
        }

        var parsed = new List<string>();
        foreach (var raw in pointer[1..].Split('/'))
        {
            var token = new System.Text.StringBuilder(raw.Length);
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] != '~')
                {
                    token.Append(raw[i]);
                    continue;
                }

                if (i + 1 >= raw.Length || raw[i + 1] is not ('0' or '1'))
                {
                    error = "Invalid escape in the JSON pointer: “~” must be followed by 0 or 1 (~0 is “~”, ~1 is “/”).";
                    return false;
                }

                token.Append(raw[i + 1] == '0' ? '~' : '/');
                i++;
            }

            parsed.Add(token.ToString());
        }

        tokens = parsed;
        return true;
    }

    /// <summary>Follows the pointer from <paramref name="root"/>. Returns false when any step does not exist.</summary>
    public static bool TryResolve(JsonElement root, IReadOnlyList<string> tokens, out JsonElement value)
    {
        value = root;
        foreach (var token in tokens)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    if (!value.TryGetProperty(token, out var property))
                        return false;
                    value = property;
                    break;

                case JsonValueKind.Array:
                    if (!TryParseIndex(token, out var index) || index >= value.GetArrayLength())
                        return false;
                    value = value[index];
                    break;

                default:
                    return false;
            }
        }

        return true;
    }

    private static bool TryParseIndex(string token, out int index)
    {
        index = 0;
        if (token.Length == 0 || token.Any(c => c is < '0' or > '9')) return false;       // also rejects "-" and signs
        if (token.Length > 1 && token[0] == '0') return false;                              // no leading zeros
        return int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index);
    }
}

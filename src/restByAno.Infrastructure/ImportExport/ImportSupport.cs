using System.Globalization;
using System.Text.Json;

namespace restByAno.Infrastructure.ImportExport;

/// <summary>
/// Result of validating an import file. Nothing is written until the whole document validated:
/// <see cref="Errors"/> make the import fail; <see cref="Warnings"/> list ignored/unsupported content that is shown to the user.
/// </summary>
public sealed record ImportOutcome<T>(
    T? Definition, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, IReadOnlyList<string> SecretsNeedingEntry) where T : class
{
    public bool Succeeded => Definition is not null && Errors.Count == 0;

    public static ImportOutcome<T> Failed(IReadOnlyList<string> errors, IReadOnlyList<string> warnings) => new(null, errors, warnings, []);
}

/// <summary>Files saved by Windows tools often start with a UTF-8 byte order mark, which the JSON parser rejects.</summary>
public static class Utf8Bom
{
    public static ReadOnlyMemory<byte> Strip(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes.AsMemory(3) : bytes.AsMemory();
}

/// <summary>Writes files through a temporary sibling and an atomic move, so an interrupted export never leaves a partial file.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string text)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, text, new System.Text.UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Only our own temp file; best effort.
            }
        }
    }
}

/// <summary>Strict reader over one JSON object: typed getters that record shape errors and, at the end, unknown fields as warnings.</summary>
internal sealed class FieldReader
{
    private readonly JsonElement _element;
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);
    private readonly List<string> _errors;
    private readonly List<string> _warnings;

    public FieldReader(JsonElement element, string path, List<string> errors, List<string> warnings)
    {
        _element = element;
        Path = path;
        _errors = errors;
        _warnings = warnings;
    }

    public string Path { get; }

    public static FieldReader? Open(JsonElement element, string path, List<string> errors, List<string> warnings)
    {
        if (element.ValueKind == JsonValueKind.Object)
            return new FieldReader(element, path, errors, warnings);
        errors.Add($"{path}: expected an object.");
        return null;
    }

    public bool Has(string name) => _element.TryGetProperty(name, out _);

    /// <summary>Marks fields as understood-and-intentionally-ignored so they are reported with a specific message.</summary>
    public void Refuse(string name, string reason)
    {
        if (_element.TryGetProperty(name, out _))
        {
            _read.Add(name);
            _warnings.Add($"{Path}.{name}: {reason}");
        }
    }

    /// <summary>Marks fields as forbidden: their presence makes the whole import fail.</summary>
    public void Forbid(string name, string reason)
    {
        if (_element.TryGetProperty(name, out _))
        {
            _read.Add(name);
            _errors.Add($"{Path}.{name}: {reason}");
        }
    }

    public string? String(string name, bool required, int maxLength = 100_000)
    {
        _read.Add(name);
        if (!_element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required) _errors.Add($"{Path}.{name}: required text is missing.");
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            _errors.Add($"{Path}.{name}: expected text.");
            return null;
        }

        var text = value.GetString()!;
        if (text.Length > maxLength)
        {
            _errors.Add($"{Path}.{name}: text is longer than {maxLength:N0} characters.");
            return null;
        }

        return text;
    }

    public int? Int(string name, bool required)
    {
        _read.Add(name);
        if (!_element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required) _errors.Add($"{Path}.{name}: required number is missing.");
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            _errors.Add($"{Path}.{name}: expected a whole number.");
            return null;
        }

        return number;
    }

    public bool? Bool(string name)
    {
        _read.Add(name);
        if (!_element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();
        _errors.Add($"{Path}.{name}: expected true or false.");
        return null;
    }

    public Guid? Guid(string name, bool required)
    {
        var text = String(name, required, 64);
        if (text is null) return null;
        if (System.Guid.TryParse(text, out var id) && id != System.Guid.Empty)
            return id;
        _errors.Add($"{Path}.{name}: not a valid id.");
        return null;
    }

    public TEnum? Enum<TEnum>(string name, bool required) where TEnum : struct, Enum
    {
        var text = String(name, required, 64);
        if (text is null) return null;
        // Names only: reject numbers such as "1" that Enum.TryParse would accept.
        if (System.Enum.GetNames<TEnum>().Any(n => n.Equals(text, StringComparison.OrdinalIgnoreCase))
            && System.Enum.TryParse<TEnum>(text, ignoreCase: true, out var value))
            return value;
        _errors.Add($"{Path}.{name}: “{Trim(text)}” is not one of {string.Join(", ", System.Enum.GetNames<TEnum>())}.");
        return null;
    }

    public JsonElement? Array(string name, bool required)
    {
        _read.Add(name);
        if (!_element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required) _errors.Add($"{Path}.{name}: required list is missing.");
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            _errors.Add($"{Path}.{name}: expected a list.");
            return null;
        }

        return value;
    }

    public JsonElement? Object(string name, bool required)
    {
        _read.Add(name);
        if (!_element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required) _errors.Add($"{Path}.{name}: required object is missing.");
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            _errors.Add($"{Path}.{name}: expected an object.");
            return null;
        }

        return value;
    }

    /// <summary>Reports every field that was not read as an ignored, unsupported field.</summary>
    public void ReportUnknown()
    {
        foreach (var property in _element.EnumerateObject())
            if (!_read.Contains(property.Name))
                _warnings.Add($"{Path}.{Trim(property.Name)}: not supported by restByAno and ignored.");
    }

    public static string Trim(string text) => text.Length <= 60 ? text : text[..60] + "…";

    public static string Index(string path, int index) => string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]");
}

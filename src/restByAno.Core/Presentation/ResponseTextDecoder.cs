using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using restByAno.Core.Models;

namespace restByAno.Core.Presentation;

public enum BodyDisplayKind
{
    Empty,
    Text,
    Binary,
}

/// <summary>Result of decoding a response body for display. The source bytes are never modified.</summary>
public sealed record DecodedBody(BodyDisplayKind Kind, string Text, string? Warning)
{
    public static DecodedBody Empty { get; } = new(BodyDisplayKind.Empty, string.Empty, null);
}

public static partial class ResponseTextDecoder
{
    private static readonly HashSet<string> TextualApplicationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json", "application/xml", "application/javascript", "application/ecmascript",
        "application/x-www-form-urlencoded", "application/yaml", "application/x-yaml", "application/graphql",
        "application/xhtml+xml",
    };

    public static DecodedBody Decode(ResponseSnapshot response)
    {
        if (response.Body.Length == 0)
            return DecodedBody.Empty;

        var mediaType = response.MediaType;
        if (mediaType is not null && !IsTextual(mediaType))
            return new DecodedBody(BodyDisplayKind.Binary, string.Empty, null);

        var charset = GetCharset(response.Headers);
        string? warning = null;
        Encoding encoding = new UTF8Encoding(false, throwOnInvalidBytes: false);
        if (charset is not null)
        {
            try
            {
                encoding = Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                warning = $"Unsupported charset '{charset}'; the body is shown as UTF-8 and may display incorrectly.";
            }
        }

        if (mediaType is null)
        {
            // No declared type: accept only strictly valid UTF-8 text without control characters.
            string strict;
            try
            {
                strict = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(response.Body.Span);
            }
            catch (DecoderFallbackException)
            {
                return new DecodedBody(BodyDisplayKind.Binary, string.Empty, null);
            }

            if (strict.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n')))
                return new DecodedBody(BodyDisplayKind.Binary, string.Empty, null);
            return new DecodedBody(BodyDisplayKind.Text, strict.TrimStart('\uFEFF'), warning);
        }

        return new DecodedBody(BodyDisplayKind.Text, encoding.GetString(response.Body.Span).TrimStart('\uFEFF'), warning);
    }

    public static bool IsJsonMediaType(string? mediaType) =>
        mediaType is not null
        && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

    private static bool IsTextual(string mediaType) =>
        mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
        || TextualApplicationTypes.Contains(mediaType);

    private static string? GetCharset(IReadOnlyList<HeaderEntry> headers)
    {
        foreach (var header in headers)
        {
            if (!header.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            var match = CharsetPattern().Match(header.Value);
            if (match.Success) return match.Groups[1].Value;
        }

        return null;
    }

    [GeneratedRegex("charset\\s*=\\s*\"?([^\";\\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CharsetPattern();
}

public static class JsonText
{
    private static readonly JsonWriterOptions IndentedOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Indents valid JSON without changing values. On failure returns false with a safe, position-only error.</summary>
    public static bool TryFormat(string text, out string formatted, out string? error)
    {
        formatted = text;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64 });
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, IndentedOptions))
                document.WriteTo(writer);
            formatted = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.LineNumber is { } line && ex.BytePositionInLine is { } pos
                ? $"not valid JSON (line {line + 1}, position {pos + 1})"
                : "not valid JSON";
            return false;
        }
    }
}

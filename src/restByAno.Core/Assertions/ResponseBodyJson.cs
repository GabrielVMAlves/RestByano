using System.Text.Json;
using restByAno.Core.Models;
using restByAno.Core.Presentation;

namespace restByAno.Core.Assertions;

/// <summary>
/// Parses a response body as JSON at most once and shares the document between all assertions and extractors of a step.
/// Invalid or unreadable bodies never throw; they produce a readable failure for JSON rules instead.
/// </summary>
public sealed class ResponseBodyJson : IDisposable
{
    private readonly ResponseSnapshot _response;
    private JsonDocument? _document;
    private string? _failure;
    private bool _parsed;

    public ResponseBodyJson(ResponseSnapshot response) => _response = response;

    public bool TryGetRoot(out JsonElement root, out string failure)
    {
        if (!_parsed)
        {
            _parsed = true;
            Parse();
        }

        root = _document is not null ? _document.RootElement : default;
        failure = _failure ?? string.Empty;
        return _document is not null;
    }

    private void Parse()
    {
        // A spooled body is only a sample in memory; rules need the complete document.
        var response = ResponseSpool.TryLoadFull(_response, ResponseSpool.MaxEvaluationBytes);
        if (response is null)
        {
            _failure = $"The response body is too large to evaluate (more than {ResponseSpool.MaxEvaluationBytes / (1024 * 1024)} MiB) or could not be read.";
            return;
        }

        var decoded = ResponseTextDecoder.Decode(response);
        if (decoded.Kind == BodyDisplayKind.Empty)
        {
            _failure = "The response has no body.";
            return;
        }

        if (decoded.Kind == BodyDisplayKind.Binary)
        {
            _failure = "The response body is not text.";
            return;
        }

        try
        {
            _document = JsonDocument.Parse(decoded.Text, new JsonDocumentOptions { MaxDepth = 256 });
        }
        catch (JsonException)
        {
            _failure = "The response body is not valid JSON.";
        }
    }

    public void Dispose() => _document?.Dispose();
}

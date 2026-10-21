using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using restByAno.Core.Models;
using restByAno.Core.Presentation;
using restByAno.Core.Running;
using restByAno.Desktop.Services;

namespace restByAno.Desktop.ViewModels;

/// <summary>One line in the Checks tab: an assertion or extraction outcome (glyph + text, never colour alone).</summary>
public sealed record CheckRowViewModel(string Glyph, string Title, string? Detail, StatusTone Tone);

public enum StatusTone
{
    None,
    Success,
    Info,
    Warning,
    Error,
}

public sealed partial class ResponseViewModel(IClipboardService clipboard, IDialogService dialogs) : ObservableObject
{
    /// <summary>Safety net for the text controls; sized above <see cref="ResponseSpool.MaxDisplayBytes"/> so a loaded body is shown whole.</summary>
    private const int DisplayCharLimit = 40_000_000;

    private string _fullRaw = string.Empty;
    private ResponseSnapshot? _response;     // the response being shown; its spool file lives until Clear
    private string _baseBanner = string.Empty;

    /// <summary>True while only a sample of a large body is shown and the rest can be loaded.</summary>
    [ObservableProperty]
    private bool _canLoadFullBody;

    /// <summary>True while the complete body sits in a temporary file that can be saved.</summary>
    [ObservableProperty]
    private bool _canSaveBody;

    [ObservableProperty]
    private string _statusGlyph = "○";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string _statusText = "No response yet";

    /// <summary>True before any request has produced (or failed to produce) a response.</summary>
    public bool IsEmpty => StatusText == "No response yet";

    [ObservableProperty]
    private StatusTone _tone = StatusTone.None;

    [ObservableProperty]
    private string _metricsText = string.Empty;

    [ObservableProperty]
    private string _bannerGlyph = "⚠";

    [ObservableProperty]
    private string _bannerText = string.Empty;

    [ObservableProperty]
    private bool _hasBanner;

    [ObservableProperty]
    private string _rawText = string.Empty;

    [ObservableProperty]
    private string _prettyText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyBodyCommand))]
    private bool _hasBodyText;

    public ObservableCollection<HeaderEntry> Headers { get; } = [];

    public ObservableCollection<CheckRowViewModel> Checks { get; } = [];

    [ObservableProperty]
    private string _checksText = string.Empty;

    [ObservableProperty]
    private StatusTone _checksTone = StatusTone.None;

    /// <summary>Shows assertion and extraction results. Details are already redacted; extracted values are never shown.</summary>
    public void ShowChecks(StepCheckResult check, RequestDefinition request)
    {
        Checks.Clear();
        foreach (var evaluation in check.Assertions)
        {
            var r = evaluation.Result;
            Checks.Add(new CheckRowViewModel(r.Passed ? "✔" : "✖", $"{DescribeAssertion(r)} — {r.Reason}", evaluation.Detail,
                r.Passed ? StatusTone.Success : StatusTone.Error));
        }

        if (check.Extraction is { } extraction)
        {
            if (extraction.Succeeded)
            {
                foreach (var x in request.Extractors)
                    Checks.Add(new CheckRowViewModel("✔", $"Extracted “{x.VariableName}” from {PointerLabel(x.JsonPointer)} (value hidden)", null, StatusTone.Success));
            }
            else
            {
                foreach (var failure in extraction.Failures)
                    Checks.Add(new CheckRowViewModel("✖", failure, null, StatusTone.Error));
            }
        }
        else if (request.Extractors.Count > 0)
        {
            Checks.Add(new CheckRowViewModel("–", "Extraction was skipped because an assertion failed.", null, StatusTone.Info));
        }

        var failed = check.Assertions.Count(a => !a.Result.Passed);
        if (check.Outcome == StepOutcome.ExtractionFailed)
        {
            ChecksText = "✖ Extraction failed";
            ChecksTone = StatusTone.Error;
        }
        else if (check.Assertions.Count == 0)
        {
            ChecksText = "○ No assertions (a pass only means the request completed)";
            ChecksTone = StatusTone.None;
            Checks.Insert(0, new CheckRowViewModel("○", "This request has no assertions, so “passed” only means the request completed.", null, StatusTone.None));
        }
        else if (failed == 0)
        {
            ChecksText = $"✔ {check.Assertions.Count} of {check.Assertions.Count} assertions passed";
            ChecksTone = StatusTone.Success;
        }
        else
        {
            ChecksText = $"✖ {failed} of {check.Assertions.Count} assertions failed";
            ChecksTone = StatusTone.Error;
        }
    }

    public void ShowChecksNotEvaluated(string reason)
    {
        Checks.Clear();
        ChecksText = "– Checks not evaluated";
        ChecksTone = StatusTone.None;
        Checks.Add(new CheckRowViewModel("–", reason, null, StatusTone.Info));
    }

    private static string PointerLabel(string pointer) => string.IsNullOrEmpty(pointer) ? "the whole body" : $"“{pointer}”";

    private static string DescribeAssertion(AssertionResult r) => r.Kind switch
    {
        AssertionKind.StatusEquals => "Status code",
        AssertionKind.HeaderEquals => $"Header “{r.HeaderName}”",
        AssertionKind.JsonExists => $"JSON path {PointerLabel(r.JsonPointer ?? string.Empty)} exists",
        AssertionKind.JsonEquals => $"JSON value at {PointerLabel(r.JsonPointer ?? string.Empty)}",
        AssertionKind.DurationBelow => "Response time",
        _ => r.Kind.ToString(),
    };

    /// <summary>Drops the previous response so a failure can never look like the previous success.</summary>
    public void Clear()
    {
        DiscardSpool();
        _response = null;
        _baseBanner = string.Empty;
        CanLoadFullBody = false;
        CanSaveBody = false;
        _fullRaw = string.Empty;
        StatusGlyph = "○";
        StatusText = "No response yet";
        Tone = StatusTone.None;
        MetricsText = string.Empty;
        SetBanner(null);
        RawText = string.Empty;
        PrettyText = string.Empty;
        HasBodyText = false;
        Headers.Clear();
        Checks.Clear();
        ChecksText = string.Empty;
        ChecksTone = StatusTone.None;
    }

    /// <summary>Deletes the temporary file behind a large body, if any. Safe to call repeatedly.</summary>
    public void DiscardSpool() => ResponseSpool.Delete(_response?.SpoolPath);

    public void BeginSending()
    {
        Clear();
        StatusGlyph = "…";
        StatusText = "Sending";
    }

    public void ShowValidation(IEnumerable<string> errors)
    {
        Clear();
        StatusGlyph = "✖";
        StatusText = "Request not sent";
        Tone = StatusTone.Error;
        SetBanner(string.Join(Environment.NewLine, errors), "✖");
    }

    public void ShowUnexpectedError(string message)
    {
        Clear();
        StatusGlyph = "✖";
        StatusText = "Unexpected error";
        Tone = StatusTone.Error;
        SetBanner(message, "✖");
    }

    public async Task ShowResultAsync(ExecutionResult result)
    {
        Clear();

        if (result.Response is { } response)
        {
            await ShowResponseAsync(response, banner: null);
            return;
        }

        var error = result.Error!;
        var (glyph, tone) = error.Kind switch
        {
            ExecutionErrorKind.Cancelled => ("⊘", StatusTone.Info),
            _ => ("✖", StatusTone.Error),
        };

        if (error.PartialResponse is { } partial)
        {
            await ShowResponseAsync(partial, $"{DescribeKind(error.Kind)}: {error.Message}");
            return;
        }

        StatusGlyph = glyph;
        StatusText = DescribeKind(error.Kind);
        Tone = tone;
        MetricsText = error.ElapsedMilliseconds is { } ms ? $"{ms} ms" : string.Empty;
        SetBanner(error.Message, glyph);
    }

    private async Task ShowResponseAsync(ResponseSnapshot response, string? banner)
    {
        var (glyph, tone) = response.StatusCode switch
        {
            >= 200 and < 300 => ("✔", StatusTone.Success),
            >= 300 and < 400 => ("➜", StatusTone.Info),
            >= 400 and < 500 => ("⚠", StatusTone.Warning),
            _ => ("✖", StatusTone.Error),
        };
        StatusGlyph = glyph;
        Tone = tone;
        StatusText = $"{response.StatusCode} {response.ReasonPhrase ?? HumanizeStatus(response.StatusCode)}".TrimEnd();
        MetricsText = $"{response.ElapsedMilliseconds} ms · {response.BodyLength:N0} bytes";

        foreach (var header in response.Headers)
            Headers.Add(header);

        _response = response;
        CanSaveBody = response.IsBodyPartial;
        CanLoadFullBody = response.IsBodyPartial && response.BodyLength <= ResponseSpool.MaxDisplayBytes;

        // Decoding and pretty-printing can be heavy for large bodies; keep it off the UI thread.
        var shown = await Task.Run(() => PrepareBody(response));
        ApplyBody(response, shown, banner, tone);
    }

    private void ApplyBody(ResponseSnapshot response, PreparedBody shown, string? banner, StatusTone tone)
    {
        _fullRaw = shown.FullRaw;
        RawText = Limit(shown.FullRaw);
        PrettyText = Limit(shown.Pretty);
        HasBodyText = shown.FullRaw.Length > 0;

        var notes = new List<string>();
        if (banner is not null) notes.Add(banner);
        notes.AddRange(shown.Notes);
        if (response.IsBodyPartial)
        {
            var size = FormatSize(response.BodyLength);
            var sample = FormatSize(response.Body.Length);
            notes.Add(CanLoadFullBody
                ? $"Large response ({size}): showing the first {sample} as a sample. Use “Load full body” to load the rest, or “Save body…” to write it to a file."
                : $"Very large response ({size}): showing the first {sample} as a sample. It is too large to display in full; use “Save body…” to write it to a file.");
            notes.Add("Copy body copies the sample shown. JSON assertions and extractions use the complete body.");
        }

        if (shown.FullRaw.Length > DisplayCharLimit || shown.Pretty.Length > DisplayCharLimit)
            notes.Add($"Only the first {DisplayCharLimit:N0} characters are displayed. Copy body copies everything.");

        _baseBanner = string.Join(Environment.NewLine, notes);
        SetBanner(notes.Count == 0 ? null : _baseBanner, tone == StatusTone.Error ? "✖" : "⚠");
    }

    private static PreparedBody PrepareBody(ResponseSnapshot response)
    {
        // A sample cut inside a multi-byte character would otherwise decode as garbage (or as "binary" without a media type).
        if (response.IsBodyPartial)
            response = response with { Body = TrimIncompleteUtf8Tail(response.Body) };

        var decoded = ResponseTextDecoder.Decode(response);
        var notes = new List<string>();
        if (decoded.Warning is not null) notes.Add(decoded.Warning);

        switch (decoded.Kind)
        {
            case BodyDisplayKind.Binary:
                notes.Add($"Binary content ({response.BodyLength:N0} bytes, {response.MediaType ?? "unknown type"}). The body is not displayed.");
                return new PreparedBody(string.Empty, string.Empty, notes);
            case BodyDisplayKind.Empty:
                return new PreparedBody(string.Empty, string.Empty, notes);
        }

        var pretty = decoded.Text;
        var looksJson = ResponseTextDecoder.IsJsonMediaType(response.MediaType);
        // A cut-off sample is never valid JSON; leave it as it is instead of reporting a misleading error.
        if (!response.IsBodyPartial && (looksJson || (response.MediaType is null && LooksLikeJson(decoded.Text))))
        {
            if (JsonText.TryFormat(decoded.Text, out var formatted, out var error))
                pretty = formatted;
            else if (looksJson)
                notes.Add($"The body is declared as JSON but is {error}. The raw text is shown in Pretty.");
        }

        return new PreparedBody(decoded.Text, pretty, notes);
    }

    /// <summary>Drops a trailing, incomplete UTF-8 sequence (at most 3 bytes) so a cut-off sample decodes cleanly.</summary>
    private static ReadOnlyMemory<byte> TrimIncompleteUtf8Tail(ReadOnlyMemory<byte> body)
    {
        var span = body.Span;
        for (var back = 1; back <= Math.Min(3, span.Length); back++)
        {
            var b = span[^back];
            if ((b & 0xC0) == 0x80) continue;                         // continuation byte: keep looking for its lead byte
            var needed = b >= 0xF0 ? 4 : b >= 0xE0 ? 3 : b >= 0xC0 ? 2 : 1;
            return needed > back ? body[..^back] : body;              // lead byte without all of its continuation bytes
        }

        return body;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):N1} GiB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):N1} MiB",
        >= 1024 => $"{bytes / 1024.0:N1} KiB",
        _ => $"{bytes:N0} bytes",
    };

    private static bool LooksLikeJson(string text)
    {
        var trimmed = text.AsSpan().TrimStart();
        return trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');
    }

    private static string Limit(string text) => text.Length <= DisplayCharLimit ? text : text[..DisplayCharLimit];

    private void SetBanner(string? text, string glyph = "⚠")
    {
        BannerText = text ?? string.Empty;
        BannerGlyph = glyph;
        HasBanner = !string.IsNullOrEmpty(text);
    }

    private static string DescribeKind(ExecutionErrorKind kind) => kind switch
    {
        ExecutionErrorKind.Validation => "Validation failed",
        ExecutionErrorKind.PolicyDenied => "Destination denied",
        ExecutionErrorKind.DnsOrConnection => "Connection failed",
        ExecutionErrorKind.Tls => "TLS failed",
        ExecutionErrorKind.Proxy => "Proxy failed",
        ExecutionErrorKind.Timeout => "Timed out",
        ExecutionErrorKind.Cancelled => "Cancelled",
        ExecutionErrorKind.ResponseLimit => "Response too large",   // legacy kind from older run history
        _ => "Transport failed",
    };

    private static string HumanizeStatus(int code) =>
        Enum.IsDefined(typeof(HttpStatusCode), code)
            ? Regex.Replace(((HttpStatusCode)code).ToString(), "(?<=[a-z])(?=[A-Z])", " ")
            : string.Empty;

    private bool CanCopyBody() => HasBodyText;

    [RelayCommand(CanExecute = nameof(CanCopyBody))]
    private void CopyBody()
    {
        if (clipboard.TrySetText(_fullRaw))
            return;
        SetBanner("The clipboard is unavailable right now. Try again.", "⚠");
    }

    [RelayCommand]
    private void ClearResponse() => Clear();

    [RelayCommand]
    private async Task LoadFullBodyAsync()
    {
        if (_response is not { IsBodyPartial: true } partial || !CanLoadFullBody) return;

        CanLoadFullBody = false;   // one load at a time; restored below if it fails
        SetBanner("Loading the full body…", "…");
        var full = await Task.Run(() => ResponseSpool.TryLoadFull(partial, ResponseSpool.MaxDisplayBytes));
        if (full is null)
        {
            CanLoadFullBody = true;
            SetBanner($"The full body could not be loaded; the temporary file may be gone. {_baseBanner}", "⚠");
            return;
        }

        // The spool file stays (it is still needed by Save body…); only the displayed text changes.
        _response = partial with { Body = full.Body };
        var shown = await Task.Run(() => PrepareBody(full));
        ApplyBody(full, shown, banner: null, Tone);
        CanSaveBody = true;
    }

    [RelayCommand]
    private async Task SaveBodyAsync()
    {
        if (_response is not { SpoolPath: { } source } response) return;
        if (!File.Exists(source))
        {
            SetBanner($"The temporary copy of the body is no longer available. {_baseBanner}", "⚠");
            return;
        }

        var extension = SuggestExtension(response.MediaType);
        var target = dialogs.PickSaveFile("Save response body", $"Response body (*{extension})|*{extension}|All files (*.*)|*.*", $"response{extension}");
        if (target is null) return;

        try
        {
            await Task.Run(() => File.Copy(source, target, overwrite: true));
            SetBanner($"{_baseBanner}{Environment.NewLine}Saved {FormatSize(response.BodyLength)} to {target}", "✔");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetBanner($"The body could not be saved ({ex.GetType().Name}). Choose another location. {_baseBanner}", "⚠");
        }
    }

    private static string SuggestExtension(string? mediaType) => mediaType switch
    {
        null => ".bin",
        _ when ResponseTextDecoder.IsJsonMediaType(mediaType) => ".json",
        _ when mediaType.EndsWith("xml", StringComparison.OrdinalIgnoreCase) => ".xml",
        _ when mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase) => ".html",
        _ when mediaType.Equals("text/csv", StringComparison.OrdinalIgnoreCase) => ".csv",
        _ when mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) => ".txt",
        _ => ".bin",
    };

    private sealed record PreparedBody(string FullRaw, string Pretty, IReadOnlyList<string> Notes);
}

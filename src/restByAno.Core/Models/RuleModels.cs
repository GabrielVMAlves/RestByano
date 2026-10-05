namespace restByAno.Core.Models;

public enum AssertionKind
{
    StatusEquals,
    HeaderEquals,
    JsonExists,
    JsonEquals,
    DurationBelow,
}

public sealed record AssertionDefinition
{
    public required Guid Id { get; init; }
    public required AssertionKind Kind { get; init; }

    /// <summary>HeaderEquals only. Matched case-insensitively.</summary>
    public string? HeaderName { get; init; }

    /// <summary>JsonExists / JsonEquals only. RFC 6901 JSON Pointer.</summary>
    public string? JsonPointer { get; init; }

    /// <summary>
    /// JsonEquals: a validated JSON scalar as JSON text (e.g. <c>"abc"</c>, <c>1</c>, <c>true</c>, <c>null</c>) so the type is preserved.
    /// HeaderEquals: the expected header value as a JSON string.
    /// </summary>
    public string? ExpectedJson { get; init; }

    /// <summary>StatusEquals only.</summary>
    public int? ExpectedStatus { get; init; }

    /// <summary>DurationBelow only. Passes when elapsed milliseconds are strictly below this value.</summary>
    public int? MaxMilliseconds { get; init; }
}

public sealed record ExtractorDefinition
{
    public required Guid Id { get; init; }
    public required string JsonPointer { get; init; }
    public required string VariableName { get; init; }

    /// <summary>Always true in the MVP: extracted values are masked and live only for the run.</summary>
    public bool Sensitive { get; init; } = true;
}

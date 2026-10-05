namespace restByAno.Core.Models;

public enum StepOutcome
{
    Passed,
    AssertionFailed,
    ExtractionFailed,
    TransportFailed,
    ValidationFailed,
    Cancelled,
    Skipped,
}

public enum RunOutcome
{
    Passed,
    Failed,
    Cancelled,
}

/// <summary>Metadata-only result of one assertion. No actual/expected values, headers or bodies.</summary>
public sealed record AssertionResult
{
    public required Guid AssertionId { get; init; }
    public required AssertionKind Kind { get; init; }
    public string? JsonPointer { get; init; }
    public string? HeaderName { get; init; }
    public required bool Passed { get; init; }

    /// <summary>Safe, human-readable reason; never contains response data.</summary>
    public required string Reason { get; init; }
}

/// <summary>Metadata-only record of one executed (or skipped) request in a run.</summary>
public sealed record StepRecord
{
    public required Guid StepId { get; init; }
    public required Guid RequestId { get; init; }
    public required string RequestName { get; init; }
    public int Sequence { get; init; }
    public bool IsCleanup { get; init; }
    public required StepOutcome Outcome { get; init; }
    public int? StatusCode { get; init; }
    public long? ElapsedMilliseconds { get; init; }
    public long? BodyBytes { get; init; }
    public ExecutionErrorKind? ErrorKind { get; init; }

    /// <summary>Safe failure explanation (validation, transport, extraction); never contains secrets or payloads.</summary>
    public string? Message { get; init; }

    /// <summary>True when the request had no assertions, so a pass reflects transport completion only.</summary>
    public bool HadNoAssertions { get; init; }

    public IReadOnlyList<AssertionResult> Assertions { get; init; } = [];
}

public sealed record RunRecord
{
    public required Guid Id { get; init; }

    /// <summary>Historical reference only; the collection may since have been deleted.</summary>
    public required Guid CollectionId { get; init; }

    public Guid? EnvironmentId { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required DateTimeOffset EndedUtc { get; init; }
    public required RunOutcome Outcome { get; init; }

    /// <summary>Identity aid computed from secret-free collection/environment structure.</summary>
    public required string SnapshotHash { get; init; }

    public IReadOnlyList<StepRecord> Steps { get; init; } = [];
}

/// <summary>Lightweight list entry for collections, environments and runs.</summary>
public sealed record EntitySummary(Guid Id, string Name, DateTimeOffset UpdatedUtc);

namespace restByAno.Core.Models;

public readonly record struct HeaderEntry(string Name, string Value);

/// <summary>
/// Fully resolved, memory-only request ready to send. It contains resolved secrets and must never be persisted,
/// exported, logged or serialized: it is deliberately not a record, and <see cref="ToString"/> reveals nothing.
/// </summary>
public sealed class ResolvedRequest
{
    public required HttpMethodKind Method { get; init; }
    public required Uri Uri { get; init; }

    /// <summary>User and auth headers in send order, general and content headers mixed; the executor routes them.</summary>
    public IReadOnlyList<HeaderEntry> Headers { get; init; } = [];

    public ReadOnlyMemory<byte> Body { get; init; }
    public bool HasBody { get; init; }
    public string? ContentType { get; init; }
    public required TransportProfile Transport { get; init; }

    public override string ToString() => $"ResolvedRequest {Method} (details withheld)";
}

public sealed record ResponseSnapshot
{
    public required int StatusCode { get; init; }
    public string? ReasonPhrase { get; init; }

    /// <summary>Response headers followed by content headers, in received order.</summary>
    public IReadOnlyList<HeaderEntry> Headers { get; init; } = [];

    /// <summary>Decoded body, at most the configured cap. Empty when the body was discarded.</summary>
    public ReadOnlyMemory<byte> Body { get; init; }

    public string? MediaType { get; init; }
    public long ElapsedMilliseconds { get; init; }
}

public enum ExecutionErrorKind
{
    Validation,
    PolicyDenied,
    DnsOrConnection,
    Tls,
    Proxy,
    Timeout,
    Cancelled,
    ResponseLimit,
    OtherTransport,
}

/// <summary>A structured failure with a message that is safe to show and store (no secrets, URLs or payloads).</summary>
public sealed record ExecutionError(ExecutionErrorKind Kind, string Message)
{
    /// <summary>Header and timing metadata retained when only the body could not be used (e.g. ResponseLimit). Body is empty.</summary>
    public ResponseSnapshot? PartialResponse { get; init; }

    public long? ElapsedMilliseconds { get; init; }
}

/// <summary>Exactly one of <see cref="Response"/> and <see cref="Error"/> is set.</summary>
public sealed record ExecutionResult
{
    public ResponseSnapshot? Response { get; private init; }
    public ExecutionError? Error { get; private init; }

    public bool Succeeded => Response is not null;

    public static ExecutionResult FromResponse(ResponseSnapshot response) => new() { Response = response };

    public static ExecutionResult FromError(ExecutionError error) => new() { Error = error };

    public static ExecutionResult FromError(ExecutionErrorKind kind, string message) =>
        new() { Error = new ExecutionError(kind, message) };
}

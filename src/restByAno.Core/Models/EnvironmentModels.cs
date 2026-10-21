using System.Text.Json.Serialization;

namespace restByAno.Core.Models;

public enum ProxyMode
{
    System,
    Explicit,
}

public sealed record EnvironmentDefinition
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<VariableDefinition> Variables { get; init; } = [];
    public TransportProfile TransportProfile { get; init; } = new();
}

/// <summary>A variable holds either a plain <see cref="Value"/> or a <see cref="SecretId"/> reference — exactly one.</summary>
public sealed record VariableDefinition
{
    public required string Name { get; init; }
    public string? Value { get; init; }
    public Guid? SecretId { get; init; }

    [JsonIgnore]
    public bool IsWellFormed => (Value is not null) ^ (SecretId is not null);

    [JsonIgnore]
    public bool IsSecret => SecretId is not null;
}

public sealed record TransportProfile
{
    public const int DefaultTimeoutSeconds = 30;

    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;
    public ProxyMode ProxyMode { get; init; } = ProxyMode.System;
    public string? ProxyUri { get; init; }
    public bool AllowNonLoopbackHttp { get; init; }

    /// <summary>
    /// The profile used for one send: timeout comes from the app settings, proxy and the non-loopback-HTTP
    /// switch from the active environment's profile (system proxy and HTTPS-only when no environment is active).
    /// </summary>
    public static TransportProfile ForSend(int timeoutSeconds, TransportProfile? environmentProfile) => new()
    {
        TimeoutSeconds = timeoutSeconds,
        ProxyMode = environmentProfile?.ProxyMode ?? ProxyMode.System,
        ProxyUri = environmentProfile?.ProxyUri,
        AllowNonLoopbackHttp = environmentProfile?.AllowNonLoopbackHttp ?? false,
    };
}

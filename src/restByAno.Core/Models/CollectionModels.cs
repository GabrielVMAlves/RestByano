namespace restByAno.Core.Models;

public enum HttpMethodKind
{
    Get,
    Post,
    Put,
    Patch,
    Delete,
    Head,
    Options,
}

public enum BodyKind
{
    None,
    Text,
    Json,
}

public enum AuthKind
{
    None,
    Bearer,
    Basic,
    ApiKey,
}

public enum ApiKeyLocation
{
    Header,
    Query,
}

/// <summary>A user-authored, secret-free collection. Persisted as versioned JSON; enums are stored by name.</summary>
public sealed record CollectionDefinition
{
    public const int CurrentSchemaVersion = 1;

    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Non-secret default variables only: every entry must have <see cref="VariableDefinition.Value"/> and no SecretId.</summary>
    public IReadOnlyList<VariableDefinition> Defaults { get; init; } = [];

    public IReadOnlyList<FolderDefinition> Folders { get; init; } = [];
    public IReadOnlyList<RequestDefinition> Requests { get; init; } = [];
}

public sealed record FolderDefinition
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public int Order { get; init; }
}

public sealed record RequestDefinition
{
    public required Guid Id { get; init; }

    /// <summary>Null means the request lives at the collection root.</summary>
    public Guid? FolderId { get; init; }

    public int Order { get; init; }
    public required string Name { get; init; }
    public HttpMethodKind Method { get; init; } = HttpMethodKind.Get;
    public string UrlTemplate { get; init; } = string.Empty;
    public IReadOnlyList<KeyValueEntry> QueryParameters { get; init; } = [];
    public IReadOnlyList<KeyValueEntry> Headers { get; init; } = [];
    public BodyDefinition Body { get; init; } = new();
    public AuthDefinition Auth { get; init; } = new();
    public IReadOnlyList<AssertionDefinition> Assertions { get; init; } = [];
    public IReadOnlyList<ExtractorDefinition> Extractors { get; init; } = [];

    /// <summary>Cleanup requests run once after the ordinary requests, even when those failed.</summary>
    public bool IsCleanup { get; init; }
}

public sealed record KeyValueEntry
{
    public required string Name { get; init; }
    public string Value { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
}

public sealed record BodyDefinition
{
    public BodyKind Kind { get; init; } = BodyKind.None;
    public string Content { get; init; } = string.Empty;

    /// <summary>Null selects the default for the kind (text/plain or application/json).</summary>
    public string? ContentType { get; init; }
}

/// <summary>Authentication by reference: the fields name variables; values are bound by the environment, never stored here.</summary>
public sealed record AuthDefinition
{
    public AuthKind Kind { get; init; } = AuthKind.None;
    public string? TokenVariable { get; init; }
    public string? UsernameVariable { get; init; }
    public string? PasswordVariable { get; init; }
    public string? ApiKeyName { get; init; }
    public string? ApiKeyVariable { get; init; }
    public ApiKeyLocation? ApiKeyLocation { get; init; }
}

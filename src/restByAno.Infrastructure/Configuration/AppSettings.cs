namespace restByAno.Infrastructure.Configuration;

/// <summary>User settings stored in restByAno.settings.json. Missing fields take these defaults.</summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public const int MinTimeoutSeconds = 1;
    public const int MaxTimeoutSeconds = 600;
    public const int MinResponseBytes = 1024;
    public const int MaxResponseBytes = 100 * 1024 * 1024;
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 3650;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>True once a database has been created for this installation. If true and the database is missing, the user must choose Restore or Create new.</summary>
    public bool InstallationInitialized { get; init; }

    public int TimeoutSeconds { get; init; } = 30;
    public int ResponseLimitBytes { get; init; } = 10 * 1024 * 1024;
    public int RetentionDays { get; init; } = 30;
    public Guid? SelectedEnvironmentId { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (TimeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds)
            errors.Add($"timeoutSeconds must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}.");
        if (ResponseLimitBytes is < MinResponseBytes or > MaxResponseBytes)
            errors.Add($"responseLimitBytes must be between {MinResponseBytes} (1 KiB) and {MaxResponseBytes} (100 MiB).");
        if (RetentionDays is < MinRetentionDays or > MaxRetentionDays)
            errors.Add($"retentionDays must be between {MinRetentionDays} and {MaxRetentionDays}.");
        return errors;
    }
}

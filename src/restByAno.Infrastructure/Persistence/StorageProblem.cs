namespace restByAno.Infrastructure.Persistence;

public enum StorageProblemKind
{
    DirectoryUnavailable,
    Unwritable,
    SettingsCorrupt,
    SettingsNewer,
    SettingsInvalid,
    DatabaseCorrupt,
    DatabaseLocked,
    DatabaseNewer,
    DatabaseNotRecognized,
    DatabaseUnavailable,
    MigrationFailed,
    RestoreFailed,
    Other,
}

/// <summary>An actionable, secret-free description of why storage cannot be used. restByAno never "fixes" these by overwriting files.</summary>
public sealed record StorageProblem(StorageProblemKind Kind, string Title, string Message);

/// <summary>Thrown for expected storage failures that carry a user-facing explanation.</summary>
public sealed class StorageException(StorageProblem problem, Exception? inner = null)
    : Exception(problem.Message, inner)
{
    public StorageProblem Problem { get; } = problem;
}

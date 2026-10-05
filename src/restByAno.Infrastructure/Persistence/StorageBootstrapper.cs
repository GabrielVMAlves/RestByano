using restByAno.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;

namespace restByAno.Infrastructure.Persistence;

public enum StorageStatus
{
    /// <summary>Directory, settings and database are usable.</summary>
    Ready,

    /// <summary>Settings say an installation exists but its database file is gone: the user must choose Restore or Create new.</summary>
    MissingDatabase,

    /// <summary>Storage cannot be used; see <see cref="StorageBootstrapResult.Problem"/>. Nothing was overwritten.</summary>
    Failed,
}

public sealed record StorageBootstrapResult(StorageStatus Status, AppSettings? Settings, StorageProblem? Problem)
{
    public static StorageBootstrapResult Ready(AppSettings settings) => new(StorageStatus.Ready, settings, null);
    public static StorageBootstrapResult MissingDatabase(AppSettings settings) => new(StorageStatus.MissingDatabase, settings, null);
    public static StorageBootstrapResult Failed(StorageProblem problem) => new(StorageStatus.Failed, null, problem);
}

/// <summary>
/// Startup sequence for the data directory: prepare folder, load settings, create/open/migrate the database.
/// First launch initializes explicitly; a missing database of a known installation is surfaced, never silently recreated.
/// </summary>
public sealed class StorageBootstrapper(StorageOptions options, SettingsStore settingsStore, DatabaseInitializer initializer)
{
    public StorageBootstrapResult Run()
    {
        try
        {
            PrepareDirectory();
            CleanOwnTempFiles();

            var load = settingsStore.Load();
            if (load.Problem is not null)
                return StorageBootstrapResult.Failed(load.Problem);

            if (!File.Exists(options.DatabasePath))
            {
                if (load.Settings is { InstallationInitialized: true })
                    return StorageBootstrapResult.MissingDatabase(load.Settings);

                initializer.CreateNew();
                return Initialized(load.Settings);
            }

            initializer.OpenAndMigrate();
            return load.Settings is { InstallationInitialized: true } existing
                ? StorageBootstrapResult.Ready(existing)
                : Initialized(load.Settings);
        }
        catch (StorageException ex)
        {
            return StorageBootstrapResult.Failed(ex.Problem);
        }
    }

    /// <summary>Creates a new empty database after the user chose "Create new". Refuses if a database exists.</summary>
    public StorageBootstrapResult CreateNewDatabase()
    {
        try
        {
            PrepareDirectory();
            var load = settingsStore.Load();
            if (load.Problem is not null)
                return StorageBootstrapResult.Failed(load.Problem);

            initializer.CreateNew();
            return Initialized(load.Settings);
        }
        catch (InvalidOperationException)
        {
            return StorageBootstrapResult.Failed(new StorageProblem(StorageProblemKind.Other, "A database already exists",
                $"'{options.DatabasePath}' already exists, so it was not replaced."));
        }
        catch (StorageException ex)
        {
            return StorageBootstrapResult.Failed(ex.Problem);
        }
    }

    /// <summary>Restores a chosen backup file as the database (only when no database exists), then validates and migrates it.</summary>
    public StorageBootstrapResult RestoreFromBackup(string backupPath)
    {
        try
        {
            PrepareDirectory();
            if (File.Exists(options.DatabasePath))
                return StorageBootstrapResult.Failed(new StorageProblem(StorageProblemKind.RestoreFailed, "A database already exists",
                    $"'{options.DatabasePath}' already exists, so nothing was restored over it."));

            var load = settingsStore.Load();
            if (load.Problem is not null)
                return StorageBootstrapResult.Failed(load.Problem);

            ValidateBackup(backupPath);

            var temp = $"{options.DatabasePath}.restoring-{Guid.NewGuid():N}.tmp";
            try
            {
                File.Copy(backupPath, temp);
                File.Move(temp, options.DatabasePath); // never overwrites
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return StorageBootstrapResult.Failed(new StorageProblem(StorageProblemKind.RestoreFailed, "Backup could not be restored",
                    $"'{backupPath}' could not be copied to '{options.DatabasePath}' ({ex.GetType().Name}). Check permissions and free space."));
            }
            finally
            {
                TryDelete(temp);
            }

            initializer.OpenAndMigrate();
            return Initialized(load.Settings);
        }
        catch (StorageException ex)
        {
            return StorageBootstrapResult.Failed(ex.Problem);
        }
    }

    private void ValidateBackup(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new StorageException(new StorageProblem(StorageProblemKind.RestoreFailed, "Backup not found", $"'{backupPath}' does not exist."));

        try
        {
            using var connection = SqliteConnectionFactory.Open(backupPath, SqliteOpenMode.ReadOnly);
            var version = DatabaseInitializer.ReadVersion(connection);
            if (version is null)
                throw new StorageException(new StorageProblem(StorageProblemKind.RestoreFailed, "Not a restByAno backup",
                    $"'{backupPath}' is not a restByAno database, so it was not restored."));
            if (version > initializer.CurrentVersion)
                throw new StorageException(new StorageProblem(StorageProblemKind.RestoreFailed, "Backup is from a newer restByAno",
                    $"'{backupPath}' uses schema version {version}; this restByAno understands up to {initializer.CurrentVersion}. Nothing was restored."));
        }
        catch (SqliteException ex)
        {
            throw new StorageException(new StorageProblem(StorageProblemKind.RestoreFailed, "Backup cannot be read",
                $"'{backupPath}' could not be read as a restByAno database ({ex.Message}). Nothing was restored."), ex);
        }
    }

    private StorageBootstrapResult Initialized(AppSettings? existing)
    {
        var settings = (existing ?? new AppSettings()) with { InstallationInitialized = true };
        settingsStore.Save(settings);
        return StorageBootstrapResult.Ready(settings);
    }

    private void PrepareDirectory()
    {
        try
        {
            Directory.CreateDirectory(options.DataDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new StorageException(new StorageProblem(StorageProblemKind.DirectoryUnavailable, "Data folder is not available",
                $"The data folder '{options.DataDirectory}' does not exist and could not be created ({ex.GetType().Name}). Create it, fix its permissions, or start restByAno with --data-dir <absolute folder>."), ex);
        }

        var probe = Path.Combine(options.DataDirectory, $".restByAno-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "x");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(new StorageProblem(StorageProblemKind.Unwritable, "Data folder is not writable",
                $"restByAno cannot write to '{options.DataDirectory}' ({ex.GetType().Name}). Fix the folder permissions or start restByAno with --data-dir <absolute folder you can write to>. No existing files were changed."), ex);
        }
        finally
        {
            TryDelete(probe);
        }
    }

    /// <summary>Removes only leftovers that this app creates (temporary write probes and temp copies).</summary>
    private void CleanOwnTempFiles()
    {
        var patterns = new[]
        {
            ".restByAno-write-test-*.tmp",
            $"{StorageOptions.SettingsFileName}.*.tmp",
            $"{StorageOptions.DatabaseFileName}.creating-*.tmp",
            $"{StorageOptions.DatabaseFileName}.restoring-*.tmp",
        };

        foreach (var pattern in patterns)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(options.DataDirectory, pattern))
                    TryDelete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Cleanup is best effort.
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}

using restByAno.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;

namespace restByAno.Infrastructure.Persistence;

/// <summary>
/// Creates a new database or validates and migrates an existing one. An existing database that is corrupt, locked,
/// unrecognised or from a newer version is reported and left exactly as it was.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly StorageOptions _options;
    private readonly IReadOnlyList<Migration> _migrations;

    public DatabaseInitializer(StorageOptions options, IReadOnlyList<Migration>? migrations = null)
    {
        _options = options;
        _migrations = (migrations ?? Migrations.All).OrderBy(m => m.Version).ToList();
    }

    public int CurrentVersion => _migrations[^1].Version;

    /// <summary>Path of the pre-migration backup made by the last <see cref="OpenAndMigrate"/> call, if any.</summary>
    public string? LastBackupPath { get; private set; }

    /// <summary>Creates a brand-new database. Refuses to touch an existing file.</summary>
    public void CreateNew()
    {
        var target = _options.DatabasePath;
        if (File.Exists(target))
            throw new InvalidOperationException("A database already exists; refusing to replace it.");

        var temp = $"{target}.creating-{Guid.NewGuid():N}.tmp";
        try
        {
            using (var connection = SqliteConnectionFactory.Open(temp, SqliteOpenMode.ReadWriteCreate))
                Apply(connection, fromVersion: 0);

            File.Move(temp, target); // fails if a file appeared meanwhile; never overwrites
        }
        catch (SqliteException ex)
        {
            throw new StorageException(SqliteConnectionFactory.Describe(ex, target, _options.BackupDirectory), ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(new StorageProblem(StorageProblemKind.Unwritable, "Database cannot be created",
                $"A new database could not be created at '{target}' ({ex.GetType().Name}). Check that the folder is writable and has free space."), ex);
        }
        finally
        {
            DeleteOwnTemp(temp);
        }
    }

    /// <summary>Validates the existing database and migrates it forward after taking a backup. Throws <see cref="StorageException"/> on any problem.</summary>
    public void OpenAndMigrate()
    {
        LastBackupPath = null;
        var path = _options.DatabasePath;
        try
        {
            using var connection = SqliteConnectionFactory.Open(path, SqliteOpenMode.ReadWrite);
            var version = ReadVersion(connection)
                          ?? throw new StorageException(new StorageProblem(StorageProblemKind.DatabaseNotRecognized, "Not a restByAno database",
                              $"'{path}' is a database file but not one created by restByAno. restByAno did not change it. Choose a different data folder with --data-dir, or move the file aside."));

            if (version > CurrentVersion)
                throw new StorageException(new StorageProblem(StorageProblemKind.DatabaseNewer, "Database is from a newer restByAno",
                    $"'{path}' uses schema version {version}, but this restByAno understands up to version {CurrentVersion}. The database was not changed. Install a newer restByAno, or choose another data folder with --data-dir."));

            if (version == CurrentVersion)
                return;

            LastBackupPath = Backup(connection, version);
            try
            {
                Apply(connection, version);
            }
            catch (SqliteException ex)
            {
                throw new StorageException(new StorageProblem(StorageProblemKind.MigrationFailed, "Database upgrade failed",
                    $"Upgrading '{path}' from version {version} failed and was rolled back ({ex.Message}). A backup taken before the upgrade is at '{LastBackupPath}'."), ex);
            }
        }
        catch (SqliteException ex)
        {
            throw new StorageException(SqliteConnectionFactory.Describe(ex, path, _options.BackupDirectory), ex);
        }
    }

    /// <summary>Reads the schema version, or null if this is not a restByAno database.</summary>
    public static int? ReadVersion(SqliteConnection connection)
    {
        using var probe = connection.CreateCommand();
        probe.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_info';";
        if (Convert.ToInt64(probe.ExecuteScalar()) == 0)
            return null;

        using var read = connection.CreateCommand();
        read.CommandText = "SELECT version FROM schema_info;";
        var value = read.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    private void Apply(SqliteConnection connection, int fromVersion)
    {
        using var transaction = connection.BeginTransaction();
        foreach (var migration in _migrations.Where(m => m.Version > fromVersion))
        {
            Execute(connection, transaction, migration.Sql);
            Execute(connection, transaction, "DELETE FROM schema_info;");
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO schema_info (version) VALUES ($v);";
            insert.Parameters.AddWithValue("$v", migration.Version);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Copies the database with SQLite's backup API into the app-specific backup directory.</summary>
    private string Backup(SqliteConnection source, int version)
    {
        Directory.CreateDirectory(_options.BackupDirectory);
        var name = $"restByAno-v{version}-{DateTime.UtcNow:yyyyMMddTHHmmssfff}Z.db";
        var destination = Path.Combine(_options.BackupDirectory, name);
        using (var target = SqliteConnectionFactory.Open(destination, SqliteOpenMode.ReadWriteCreate))
            source.BackupDatabase(target);
        return destination;
    }

    private static void DeleteOwnTemp(string temp)
    {
        try
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only this app's own temp file; cleaned on the next start if it cannot be removed now.
        }
    }
}

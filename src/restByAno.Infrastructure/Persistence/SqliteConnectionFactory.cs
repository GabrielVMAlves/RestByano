using restByAno.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;

namespace restByAno.Infrastructure.Persistence;

/// <summary>Opens connections to an existing restByAno database with foreign keys on and a busy timeout.</summary>
public sealed class SqliteConnectionFactory(StorageOptions options)
{
    public const int BusyTimeoutMilliseconds = 5000;

    public SqliteConnection Open() => Open(options.DatabasePath, SqliteOpenMode.ReadWrite);

    public static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false, // pooled handles would keep the file open and block backup/restore/replace
            ForeignKeys = true,
            DefaultTimeout = BusyTimeoutMilliseconds / 1000, // command wait; defaults to 30 s, far too long for a locked file
        }.ToString());

        try
        {
            connection.Open();
            using var pragma = connection.CreateCommand();
            pragma.CommandText = $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};";
            pragma.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Maps a SQLite failure to an actionable problem; the database file is never modified by this mapping.</summary>
    public static StorageProblem Describe(SqliteException ex, string databasePath, string backupDirectory) => ex.SqliteErrorCode switch
    {
        26 or 11 => new StorageProblem(StorageProblemKind.DatabaseCorrupt, "Database is damaged",
            $"'{databasePath}' is not a readable restByAno database (it may be damaged or not a database at all). restByAno did not change it. Restore a backup (look in '{backupDirectory}'), or move the file aside and restart to create a new, empty database."),
        5 or 6 => new StorageProblem(StorageProblemKind.DatabaseLocked, "Database is locked",
            $"'{databasePath}' is locked by another program or process. Close anything that has it open (another restByAno, a backup tool or a database browser) and try again."),
        8 or 3 or 14 or 13 or 10 => new StorageProblem(StorageProblemKind.Unwritable, "Database cannot be opened for writing",
            $"'{databasePath}' could not be opened for reading and writing ({ex.Message}). Check folder permissions, free disk space and that the file is not read-only."),
        _ => new StorageProblem(StorageProblemKind.DatabaseUnavailable, "Database could not be used",
            $"'{databasePath}' could not be used: {ex.Message}. restByAno did not change the file."),
    };
}

namespace restByAno.Infrastructure.Persistence;

/// <summary>One forward-only schema step. <see cref="Sql"/> may contain several statements.</summary>
public sealed record Migration(int Version, string Sql);

public static class Migrations
{
    public static IReadOnlyList<Migration> All { get; } =
    [
        new(1, """
            CREATE TABLE schema_info (
                version INTEGER NOT NULL
            );

            CREATE TABLE collections (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                definition_json TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE environments (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                definition_json TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE secrets (
                id TEXT PRIMARY KEY,
                environment_id TEXT NOT NULL REFERENCES environments(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                ciphertext BLOB NOT NULL,
                UNIQUE (environment_id, name)
            );

            -- collection_id is a historical reference, deliberately not a foreign key.
            CREATE TABLE runs (
                id TEXT PRIMARY KEY,
                collection_id TEXT NOT NULL,
                environment_id TEXT,
                started_utc TEXT NOT NULL,
                ended_utc TEXT NOT NULL,
                outcome TEXT NOT NULL,
                snapshot_hash TEXT NOT NULL,
                result_json TEXT NOT NULL
            );

            CREATE INDEX ix_runs_collection ON runs (collection_id, started_utc);
            CREATE INDEX ix_runs_ended ON runs (ended_utc);
            """),
    ];
}

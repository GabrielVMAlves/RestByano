using System.Globalization;
using System.Text.Json;
using restByAno.Core.Contracts;
using restByAno.Core.Models;
using Microsoft.Data.Sqlite;

namespace restByAno.Infrastructure.Persistence;

/// <summary>
/// Stores run history as metadata only: ids, timestamps, outcome, snapshot hash and a JSON list of step records (status, timing,
/// sizes, assertion kind/path/result and safe reasons). No raw requests or responses and no extracted values exist in the model.
/// The collection id is a historical reference, not a foreign key, so deleting a collection does not erase its history.
/// </summary>
public sealed class RunRepository(SqliteConnectionFactory factory) : IRunRepository
{
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public Task SaveAsync(RunRecord run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var resultJson = JsonSerializer.Serialize(run.Steps, StorageJson.Options);
        return Run(() =>
        {
            using var connection = factory.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO runs (id, collection_id, environment_id, started_utc, ended_utc, outcome, snapshot_hash, result_json)
                VALUES ($id, $collection, $environment, $started, $ended, $outcome, $hash, $json)
                ON CONFLICT(id) DO UPDATE SET ended_utc = excluded.ended_utc, outcome = excluded.outcome,
                    snapshot_hash = excluded.snapshot_hash, result_json = excluded.result_json;
                """;
            command.Parameters.AddWithValue("$id", run.Id.ToString());
            command.Parameters.AddWithValue("$collection", run.CollectionId.ToString());
            command.Parameters.AddWithValue("$environment", run.EnvironmentId?.ToString() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$started", Format(run.StartedUtc));
            command.Parameters.AddWithValue("$ended", Format(run.EndedUtc));
            command.Parameters.AddWithValue("$outcome", run.Outcome.ToString());
            command.Parameters.AddWithValue("$hash", run.SnapshotHash);
            command.Parameters.AddWithValue("$json", resultJson);
            command.ExecuteNonQuery();
            transaction.Commit();
            return 0;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<RunRecord>> ListAsync(Guid? collectionId, int limit, CancellationToken cancellationToken) =>
        Run<IReadOnlyList<RunRecord>>(() =>
        {
            using var connection = factory.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, collection_id, environment_id, started_utc, ended_utc, outcome, snapshot_hash, result_json
                FROM runs
                WHERE ($collection IS NULL OR collection_id = $collection)
                ORDER BY started_utc DESC, id
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$collection", collectionId?.ToString() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 10_000));
            using var reader = command.ExecuteReader();
            var runs = new List<RunRecord>();
            while (reader.Read())
            {
                IReadOnlyList<StepRecord> steps;
                try
                {
                    steps = JsonSerializer.Deserialize<List<StepRecord>>(reader.GetString(7), StorageJson.Options) ?? [];
                }
                catch (JsonException)
                {
                    steps = []; // a damaged history row must not break the list; its summary fields still show
                }

                runs.Add(new RunRecord
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    CollectionId = Guid.Parse(reader.GetString(1)),
                    EnvironmentId = reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                    StartedUtc = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    EndedUtc = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    Outcome = Enum.TryParse<RunOutcome>(reader.GetString(5), out var outcome) ? outcome : RunOutcome.Failed,
                    SnapshotHash = reader.GetString(6),
                    Steps = steps,
                });
            }

            return runs;
        }, cancellationToken);

    /// <summary>Deletes only rows of the runs table that ended strictly before the cutoff. Collections and environments are untouched.</summary>
    public Task<int> PurgeAsync(DateTimeOffset endedBeforeUtc, CancellationToken cancellationToken) =>
        Run(() =>
        {
            using var connection = factory.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM runs WHERE ended_utc < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", Format(endedBeforeUtc));
            var deleted = command.ExecuteNonQuery();
            transaction.Commit();
            return deleted;
        }, cancellationToken);

    private static Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return work();
            }
            catch (SqliteException ex)
            {
                throw new StorageException(SqliteConnectionFactory.Describe(ex, "the restByAno database", "the backup folder"), ex);
            }
        }, cancellationToken);
}

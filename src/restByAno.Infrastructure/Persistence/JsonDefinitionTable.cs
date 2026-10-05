using System.Globalization;
using System.Text.Json;
using restByAno.Core.Models;
using Microsoft.Data.Sqlite;

namespace restByAno.Infrastructure.Persistence;

/// <summary>Shared CRUD for tables that store a named definition as versioned JSON (collections, environments).</summary>
internal sealed class JsonDefinitionTable(SqliteConnectionFactory factory, string tableName, string noun)
{
    public Task SaveAsync<T>(Guid id, string name, T definition, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"A {noun} needs a name.", nameof(name));

        var json = JsonSerializer.Serialize(definition, StorageJson.Options);
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return Run(() =>
        {
            using var connection = factory.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO {tableName} (id, name, definition_json, updated_utc)
                VALUES ($id, $name, $json, $now)
                ON CONFLICT(id) DO UPDATE SET name = excluded.name,
                    definition_json = excluded.definition_json, updated_utc = excluded.updated_utc;
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
            transaction.Commit();
        }, cancellationToken);
    }

    public Task<T?> LoadAsync<T>(Guid id, Func<T, int> schemaVersionOf, int supportedVersion, CancellationToken cancellationToken)
        where T : class =>
        Run(() =>
        {
            using var connection = factory.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT definition_json FROM {tableName} WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            if (command.ExecuteScalar() is not string json)
                return null;

            T? definition;
            try
            {
                definition = JsonSerializer.Deserialize<T>(json, StorageJson.Options);
            }
            catch (JsonException ex)
            {
                throw Corrupt(id, ex);
            }

            if (definition is null)
                throw Corrupt(id, null);
            if (schemaVersionOf(definition) > supportedVersion)
                throw new StorageException(new StorageProblem(StorageProblemKind.DatabaseNewer, $"The {noun} is from a newer restByAno",
                    $"This {noun} was saved in format version {schemaVersionOf(definition)}, which this restByAno cannot read. It was not changed."));
            return definition;
        }, cancellationToken);

    public Task<IReadOnlyList<EntitySummary>> ListAsync(CancellationToken cancellationToken) =>
        Run<IReadOnlyList<EntitySummary>>(() =>
        {
            using var connection = factory.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT id, name, updated_utc FROM {tableName} ORDER BY name COLLATE NOCASE, id;";
            using var reader = command.ExecuteReader();
            var list = new List<EntitySummary>();
            while (reader.Read())
            {
                list.Add(new EntitySummary(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
            }

            return list;
        }, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        Run(() =>
        {
            using var connection = factory.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {tableName} WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.ExecuteNonQuery();
            transaction.Commit();
        }, cancellationToken);

    private StorageException Corrupt(Guid id, Exception? inner) =>
        new(new StorageProblem(StorageProblemKind.DatabaseCorrupt, $"A saved {noun} is damaged",
            $"The saved {noun} '{id}' could not be read. It was not changed; other data is unaffected."), inner);

    /// <summary>Runs blocking SQLite work off the calling (UI) thread and translates SQLite failures.</summary>
    private Task Run(Action work, CancellationToken cancellationToken) =>
        Run<object?>(() =>
        {
            work();
            return null;
        }, cancellationToken);

    private Task<TResult> Run<TResult>(Func<TResult> work, CancellationToken cancellationToken) =>
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

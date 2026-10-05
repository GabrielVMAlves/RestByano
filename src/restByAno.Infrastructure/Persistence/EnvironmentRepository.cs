using System.Globalization;
using System.Text.Json;
using restByAno.Core.Contracts;
using restByAno.Core.Models;
using restByAno.Infrastructure.Secrets;
using Microsoft.Data.Sqlite;

namespace restByAno.Infrastructure.Persistence;

public sealed class EnvironmentRepository(SqliteConnectionFactory factory) : IEnvironmentRepository
{
    private readonly JsonDefinitionTable _table = new(factory, "environments", "environment");

    public Task<EnvironmentDefinition?> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        _table.LoadAsync<EnvironmentDefinition>(id, _ => 1, 1, cancellationToken);

    public Task<IReadOnlyList<EntitySummary>> ListAsync(CancellationToken cancellationToken) =>
        _table.ListAsync(cancellationToken);

    public Task SaveAsync(EnvironmentDefinition environment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.Variables.Any(v => !v.IsWellFormed))
            throw new ArgumentException("Each variable must have exactly one of a value or a secret reference.", nameof(environment));
        return _table.SaveAsync(environment.Id, environment.Name, environment, cancellationToken);
    }

    public Task SaveAsync(EnvironmentDefinition environment, IReadOnlyList<SecretUpdate> secretUpdates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(secretUpdates);
        if (string.IsNullOrWhiteSpace(environment.Name))
            throw new ArgumentException("An environment needs a name.", nameof(environment));
        if (environment.Variables.Any(v => !v.IsWellFormed))
            throw new ArgumentException("Each variable must have exactly one of a value or a secret reference.", nameof(environment));

        var json = JsonSerializer.Serialize(environment, StorageJson.Options);
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var referenced = environment.Variables.Where(v => v.SecretId is not null).Select(v => v.SecretId!.Value).ToHashSet();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var connection = factory.Open();
                using var transaction = connection.BeginTransaction();

                Execute(connection, transaction, """
                    INSERT INTO environments (id, name, definition_json, updated_utc) VALUES ($id, $name, $json, $now)
                    ON CONFLICT(id) DO UPDATE SET name = excluded.name,
                        definition_json = excluded.definition_json, updated_utc = excluded.updated_utc;
                    """,
                    ("$id", environment.Id.ToString()), ("$name", environment.Name), ("$json", json), ("$now", now));

                // Drop secrets of this environment that no variable references any more (never other environments' secrets).
                var existing = new List<string>();
                using (var list = connection.CreateCommand())
                {
                    list.Transaction = transaction;
                    list.CommandText = "SELECT id FROM secrets WHERE environment_id = $e;";
                    list.Parameters.AddWithValue("$e", environment.Id.ToString());
                    using var reader = list.ExecuteReader();
                    while (reader.Read()) existing.Add(reader.GetString(0));
                }

                foreach (var id in existing.Where(id => !referenced.Contains(Guid.Parse(id))))
                    Execute(connection, transaction, "DELETE FROM secrets WHERE id = $id AND environment_id = $e;",
                        ("$id", id), ("$e", environment.Id.ToString()));

                foreach (var update in secretUpdates.Where(u => u.NewValue is not null && referenced.Contains(u.SecretId)))
                {
                    Execute(connection, transaction, """
                        INSERT INTO secrets (id, environment_id, name, ciphertext) VALUES ($id, $e, $n, $c)
                        ON CONFLICT(id) DO UPDATE SET name = excluded.name, ciphertext = excluded.ciphertext
                        WHERE environment_id = excluded.environment_id;
                        """,
                        ("$id", update.SecretId.ToString()), ("$e", environment.Id.ToString()), ("$n", update.VariableName),
                        ("$c", DpapiProtector.Protect(update.NewValue!, update.SecretId)));
                }

                // Every secret variable must have a stored value; otherwise nothing is saved.
                foreach (var variable in environment.Variables.Where(v => v.SecretId is not null))
                {
                    using var check = connection.CreateCommand();
                    check.Transaction = transaction;
                    check.CommandText = "SELECT COUNT(*) FROM secrets WHERE id = $id AND environment_id = $e;";
                    check.Parameters.AddWithValue("$id", variable.SecretId!.Value.ToString());
                    check.Parameters.AddWithValue("$e", environment.Id.ToString());
                    if (Convert.ToInt32(check.ExecuteScalar()) == 0)
                        throw new StorageException(new StorageProblem(StorageProblemKind.Other, "A secret has no value",
                            $"The secret variable “{variable.Name}” has no stored value. Enter a value and save again; nothing was changed."));
                }

                transaction.Commit();
            }
            catch (SqliteException ex)
            {
                throw new StorageException(SqliteConnectionFactory.Describe(ex, "the restByAno database", "the backup folder"), ex);
            }
        }, cancellationToken);
    }

    public Task<IReadOnlySet<Guid>> GetStoredSecretIdsAsync(Guid environmentId, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlySet<Guid>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var connection = factory.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT id FROM secrets WHERE environment_id = $e;";
                command.Parameters.AddWithValue("$e", environmentId.ToString());
                using var reader = command.ExecuteReader();
                var ids = new HashSet<Guid>();
                while (reader.Read()) ids.Add(Guid.Parse(reader.GetString(0)));
                return ids;
            }
            catch (SqliteException ex)
            {
                throw new StorageException(SqliteConnectionFactory.Describe(ex, "the restByAno database", "the backup folder"), ex);
            }
        }, cancellationToken);

    /// <summary>Deletes the environment; its secrets are removed by the foreign key cascade.</summary>
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        _table.DeleteAsync(id, cancellationToken);

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}

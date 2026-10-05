using restByAno.Core.Contracts;
using restByAno.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace restByAno.Infrastructure.Secrets;

/// <summary>Stores secret values as DPAPI ciphertext in the <c>secrets</c> table. Plaintext is never written anywhere.</summary>
public sealed class DpapiSecretStore(SqliteConnectionFactory factory) : ISecretStore
{
    public Task<Guid> SaveAsync(Guid environmentId, string name, string value, CancellationToken cancellationToken) =>
        Run(() =>
        {
            using var connection = factory.Open();
            using var transaction = connection.BeginTransaction();

            using var find = connection.CreateCommand();
            find.Transaction = transaction;
            find.CommandText = "SELECT id FROM secrets WHERE environment_id = $e AND name = $n;";
            find.Parameters.AddWithValue("$e", environmentId.ToString());
            find.Parameters.AddWithValue("$n", name);
            var id = find.ExecuteScalar() is string existing ? Guid.Parse(existing) : Guid.NewGuid();

            using var upsert = connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO secrets (id, environment_id, name, ciphertext) VALUES ($id, $e, $n, $c)
                ON CONFLICT(id) DO UPDATE SET ciphertext = excluded.ciphertext;
                """;
            upsert.Parameters.AddWithValue("$id", id.ToString());
            upsert.Parameters.AddWithValue("$e", environmentId.ToString());
            upsert.Parameters.AddWithValue("$n", name);
            upsert.Parameters.AddWithValue("$c", DpapiProtector.Protect(value, id));
            upsert.ExecuteNonQuery();

            transaction.Commit();
            return id;
        }, cancellationToken);

    public Task<SecretReadResult> ReadAsync(Guid secretId, CancellationToken cancellationToken) =>
        Run(() =>
        {
            using var connection = factory.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ciphertext FROM secrets WHERE id = $id;";
            command.Parameters.AddWithValue("$id", secretId.ToString());
            if (command.ExecuteScalar() is not byte[] ciphertext)
                return SecretReadResult.Missing;

            return DpapiProtector.TryUnprotect(ciphertext, secretId, out var plaintext)
                ? SecretReadResult.Ok(plaintext!)
                : SecretReadResult.Unreadable;
        }, cancellationToken);

    public Task DeleteAsync(Guid secretId, CancellationToken cancellationToken) =>
        Run<object?>(() =>
        {
            using var connection = factory.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM secrets WHERE id = $id;";
            command.Parameters.AddWithValue("$id", secretId.ToString());
            command.ExecuteNonQuery();
            return null;
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

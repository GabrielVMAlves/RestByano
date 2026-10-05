using restByAno.Core.Contracts;
using restByAno.Core.Models;

namespace restByAno.Infrastructure.Persistence;

public sealed class CollectionRepository(SqliteConnectionFactory factory) : ICollectionRepository
{
    private readonly JsonDefinitionTable _table = new(factory, "collections", "collection");

    public Task<CollectionDefinition?> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        _table.LoadAsync<CollectionDefinition>(id, c => c.SchemaVersion, CollectionDefinition.CurrentSchemaVersion, cancellationToken);

    public Task<IReadOnlyList<EntitySummary>> ListAsync(CancellationToken cancellationToken) =>
        _table.ListAsync(cancellationToken);

    public Task SaveAsync(CollectionDefinition collection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (collection.Defaults.Any(v => v.SecretId is not null || v.Value is null))
            throw new ArgumentException("Collection defaults must be plain, non-secret values.", nameof(collection));
        return _table.SaveAsync(collection.Id, collection.Name, collection with { SchemaVersion = CollectionDefinition.CurrentSchemaVersion }, cancellationToken);
    }

    /// <summary>Deletes the collection only. Run history keeps its historical reference.</summary>
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        _table.DeleteAsync(id, cancellationToken);
}

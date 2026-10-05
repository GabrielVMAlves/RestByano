using restByAno.Core.Models;

namespace restByAno.Core.Contracts;

public interface IRequestExecutor
{
    /// <summary>Sends the request and returns a response or a structured error. Expected failures never throw.</summary>
    Task<ExecutionResult> ExecuteAsync(ResolvedRequest request, CancellationToken cancellationToken);
}

public interface ICollectionRepository
{
    Task<CollectionDefinition?> LoadAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<EntitySummary>> ListAsync(CancellationToken cancellationToken);
    Task SaveAsync(CollectionDefinition collection, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>A change to one secret variable of an environment. <see cref="NewValue"/> null keeps the stored secret.</summary>
public sealed record SecretUpdate(Guid SecretId, string VariableName, string? NewValue);

public interface IEnvironmentRepository
{
    Task<EnvironmentDefinition?> LoadAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<EntitySummary>> ListAsync(CancellationToken cancellationToken);
    Task SaveAsync(EnvironmentDefinition environment, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the environment and its secret changes in one transaction: new/changed secret values are encrypted and stored,
    /// and secrets of this environment that no variable references any more are removed. Every secret variable must end up
    /// with a stored value.
    /// </summary>
    Task SaveAsync(EnvironmentDefinition environment, IReadOnlyList<SecretUpdate> secretUpdates, CancellationToken cancellationToken);

    /// <summary>Ids of the secrets that actually have a stored value for this environment (an imported environment can reference secrets that still need entering).</summary>
    Task<IReadOnlySet<Guid>> GetStoredSecretIdsAsync(Guid environmentId, CancellationToken cancellationToken);

    /// <summary>Deletes the environment and, by cascade, its secrets.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public enum SecretReadStatus
{
    Ok,
    Missing,
    Unreadable,
}

public sealed record SecretReadResult(SecretReadStatus Status, string? Value)
{
    public static SecretReadResult Missing { get; } = new(SecretReadStatus.Missing, null);
    public static SecretReadResult Unreadable { get; } = new(SecretReadStatus.Unreadable, null);
    public static SecretReadResult Ok(string value) => new(SecretReadStatus.Ok, value);
}

public interface ISecretStore
{
    /// <summary>Stores or replaces the secret named <paramref name="name"/> in the environment and returns its stable id.</summary>
    Task<Guid> SaveAsync(Guid environmentId, string name, string value, CancellationToken cancellationToken);

    Task<SecretReadResult> ReadAsync(Guid secretId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid secretId, CancellationToken cancellationToken);
}

public interface IRunRepository
{
    Task SaveAsync(RunRecord run, CancellationToken cancellationToken);

    /// <summary>Newest first. A null collection id lists runs of all collections.</summary>
    Task<IReadOnlyList<RunRecord>> ListAsync(Guid? collectionId, int limit, CancellationToken cancellationToken);

    /// <summary>Deletes runs that ended before the cutoff and returns the count. Pass <see cref="DateTimeOffset.MaxValue"/> to clear all history.</summary>
    Task<int> PurgeAsync(DateTimeOffset endedBeforeUtc, CancellationToken cancellationToken);
}

public sealed record TargetPolicyResult(bool Allowed, string? Reason)
{
    public static TargetPolicyResult Allow { get; } = new(true, null);
    public static TargetPolicyResult Deny(string reason) => new(false, reason);
}

public interface ITargetPolicy
{
    /// <summary>Checks the fully resolved destination. Must be called immediately before any send.</summary>
    TargetPolicyResult Validate(Uri target, TransportProfile transport);
}

public interface IReportExporter
{
    /// <summary>Writes a metadata-only report for the run. Never includes raw traffic, extracted values or secrets.</summary>
    Task ExportAsync(RunRecord run, Stream destination, CancellationToken cancellationToken);
}

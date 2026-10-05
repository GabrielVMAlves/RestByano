using restByAno.Core.Contracts;
using restByAno.Core.Models;

namespace restByAno.Core.Resolution;

/// <summary>
/// Freezes variables at Send/Run time. Precedence: run variables &gt; environment &gt; collection defaults.
/// Secret variables are read through <see cref="ISecretStore"/>; a secret that is missing or cannot be decrypted is
/// recorded as unreadable so a request fails clearly instead of sending an empty credential.
/// </summary>
public sealed class VariableSnapshotFactory(ISecretStore? secretStore)
{
    public async Task<VariableSnapshot> BuildAsync(
        IEnumerable<VariableDefinition> collectionDefaults,
        EnvironmentDefinition? environment,
        IReadOnlyDictionary<string, string>? runVariables,
        CancellationToken cancellationToken)
    {
        var builder = VariableSnapshot.CreateBuilder();

        // Collection defaults are non-secret by definition; ignore anything malformed rather than guessing.
        builder.AddLayer(collectionDefaults
            .Where(v => VariableNames.IsValid(v.Name) && v.Value is not null && v.SecretId is null)
            .Select(v => new VariableEntry(v.Name, v.Value, IsSecret: false)));

        if (environment is not null)
        {
            var layer = new List<VariableEntry>();
            foreach (var variable in environment.Variables.Where(v => VariableNames.IsValid(v.Name)))
                layer.Add(await ReadAsync(variable, cancellationToken).ConfigureAwait(false));
            builder.AddLayer(layer);
        }

        if (runVariables is not null)
        {
            // Extracted values are sensitive for the whole run (guide: Sensitive=true is enforced in the MVP).
            builder.AddLayer(runVariables.Select(kv => new VariableEntry(kv.Key, kv.Value, IsSecret: true)));
        }

        return builder.Build();
    }

    private async Task<VariableEntry> ReadAsync(VariableDefinition variable, CancellationToken cancellationToken)
    {
        if (variable.SecretId is not { } secretId)
            return new VariableEntry(variable.Name, variable.Value, IsSecret: false);

        if (secretStore is null)
            return new VariableEntry(variable.Name, null, IsSecret: true, VariableStatus.Unreadable);

        var read = await secretStore.ReadAsync(secretId, cancellationToken).ConfigureAwait(false);
        return read.Status == SecretReadStatus.Ok
            ? new VariableEntry(variable.Name, read.Value, IsSecret: true)
            : new VariableEntry(variable.Name, null, IsSecret: true, VariableStatus.Unreadable);
    }
}

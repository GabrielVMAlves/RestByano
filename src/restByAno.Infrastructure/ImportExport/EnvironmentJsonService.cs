using System.Text.Json;
using restByAno.Core.Models;
using restByAno.Core.Resolution;

namespace restByAno.Infrastructure.ImportExport;

/// <summary>
/// Native environment import/export. Secret variables are exported as <c>{"name":…,"requiresSecret":true}</c> only — never an
/// id, ciphertext or value. On import every secret variable gets a fresh local binding without a stored value, and its name is
/// returned so the user can be asked to enter it. Transport/proxy settings are neither exported nor imported.
/// </summary>
public sealed class EnvironmentJsonService
{
    private const int MaxDepth = 64;

    public string Export(EnvironmentDefinition environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CollectionJsonService.SupportedSchemaVersion);
            writer.WriteString("kind", "environment");
            writer.WriteStartObject("definition");
            writer.WriteString("id", environment.Id);
            writer.WriteString("name", environment.Name);
            writer.WriteStartArray("variables");
            foreach (var variable in environment.Variables)
            {
                writer.WriteStartObject();
                writer.WriteString("name", variable.Name);
                if (variable.SecretId is not null)
                    writer.WriteBoolean("requiresSecret", true);   // requirement only: no id, no value
                else
                    writer.WriteString("value", variable.Value ?? string.Empty);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public Task ExportFileAsync(EnvironmentDefinition environment, string path, CancellationToken cancellationToken) =>
        Task.Run(() => AtomicFile.WriteAllText(path, Export(environment)), cancellationToken);

    public async Task<ImportOutcome<EnvironmentDefinition>> ImportFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return ImportOutcome<EnvironmentDefinition>.Failed([$"The file “{path}” does not exist."], []);
            if (info.Length > CollectionJsonService.MaxFileBytes)
                return ImportOutcome<EnvironmentDefinition>.Failed([$"The file is {info.Length / (1024.0 * 1024):N1} MiB; imports are limited to {CollectionJsonService.MaxFileBytes / (1024 * 1024)} MiB."], []);
            return Import(await File.ReadAllBytesAsync(path, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ImportOutcome<EnvironmentDefinition>.Failed([$"The file could not be read ({ex.GetType().Name})."], []);
        }
    }

    public ImportOutcome<EnvironmentDefinition> Import(byte[] utf8)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (utf8.Length > CollectionJsonService.MaxFileBytes)
            return ImportOutcome<EnvironmentDefinition>.Failed([$"The file is larger than {CollectionJsonService.MaxFileBytes / (1024 * 1024)} MiB."], warnings);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(Utf8Bom.Strip(utf8), new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line && ex.BytePositionInLine is { } pos ? $" (line {line + 1}, position {pos + 1})" : string.Empty;
            return ImportOutcome<EnvironmentDefinition>.Failed([$"The file is not valid JSON{where}, or is nested deeper than {MaxDepth} levels."], warnings);
        }

        using (document)
        {
            var envelope = FieldReader.Open(document.RootElement, "file", errors, warnings);
            if (envelope is null)
                return ImportOutcome<EnvironmentDefinition>.Failed(errors, warnings);

            var version = envelope.Int("schemaVersion", required: true);
            var kind = envelope.String("kind", required: true, 32);
            var definitionElement = envelope.Object("definition", required: true);
            envelope.ReportUnknown();

            if (version is not null && version != CollectionJsonService.SupportedSchemaVersion)
                errors.Add($"file.schemaVersion: version {version} is not supported (this restByAno reads version {CollectionJsonService.SupportedSchemaVersion}).");
            if (kind is not null && kind != "environment")
                errors.Add($"file.kind: this file is “{FieldReader.Trim(kind)}”, not an environment.");
            if (errors.Count > 0 || definitionElement is null)
                return ImportOutcome<EnvironmentDefinition>.Failed(errors, warnings);

            var d = FieldReader.Open(definitionElement.Value, "definition", errors, warnings);
            if (d is null)
                return ImportOutcome<EnvironmentDefinition>.Failed(errors, warnings);

            d.Guid("id", required: false);
            var name = d.String("name", required: true, 200);
            if (name is not null && string.IsNullOrWhiteSpace(name))
                errors.Add("definition.name: the environment needs a name.");
            d.Refuse("transportProfile", "transport and proxy settings are never imported.");
            foreach (var field in new[] { "secrets", "ciphertext" })
                d.Forbid(field, "secrets are never imported.");
            foreach (var field in new[] { "workspace", "workspaceId", "team", "owner", "account", "cloud", "sync" })
                d.Refuse(field, "cloud/account information is never imported.");

            var variables = new List<VariableDefinition>();
            var secrets = new List<string>();
            if (d.Array("variables", required: false) is { } list)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                var index = 0;
                foreach (var item in list.EnumerateArray())
                {
                    var path = FieldReader.Index("definition.variables", index++);
                    var v = FieldReader.Open(item, path, errors, warnings);
                    if (v is null) continue;
                    var variableName = v.String("name", required: true, 128);
                    var hasValue = v.Has("value");
                    var value = v.String("value", required: false, 1_000_000);
                    var requiresSecret = v.Bool("requiresSecret") ?? false;
                    v.Refuse("secretId", "secret ids are never imported; a new local secret slot is created instead.");
                    v.Forbid("ciphertext", "secrets are never imported.");
                    v.ReportUnknown();
                    if (variableName is null) continue;

                    if (!VariableNames.IsValid(variableName)) { errors.Add($"{path}.name: “{FieldReader.Trim(variableName)}” is not a valid variable name."); continue; }
                    if (!names.Add(variableName)) { errors.Add($"{path}.name: the variable “{variableName}” is defined more than once."); continue; }

                    if (requiresSecret)
                    {
                        if (hasValue) { errors.Add($"{path}: a secret variable must not carry a value; enter secrets in restByAno after importing."); continue; }
                        variables.Add(new VariableDefinition { Name = variableName, SecretId = Guid.NewGuid() });
                        secrets.Add(variableName);
                    }
                    else
                    {
                        if (value is null) { errors.Add($"{path}.value: a plain variable needs a value (or set requiresSecret)."); continue; }
                        variables.Add(new VariableDefinition { Name = variableName, Value = value });
                    }
                }
            }

            d.ReportUnknown();
            if (errors.Count > 0 || name is null)
                return ImportOutcome<EnvironmentDefinition>.Failed(errors, warnings);

            return new ImportOutcome<EnvironmentDefinition>(
                new EnvironmentDefinition { Id = Guid.NewGuid(), Name = name.Trim(), Variables = variables },
                [], warnings, secrets);
        }
    }
}

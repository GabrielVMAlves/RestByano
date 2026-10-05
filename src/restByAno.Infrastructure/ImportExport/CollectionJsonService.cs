using System.Text;
using System.Text.Json;
using restByAno.Core.Assertions;
using restByAno.Core.Models;
using restByAno.Core.Resolution;
using restByAno.Infrastructure.Persistence;

namespace restByAno.Infrastructure.ImportExport;

/// <summary>
/// Native collection import/export. Envelope: <c>{"schemaVersion":1,"kind":"collection","definition":{…}}</c>.
/// The whole document is validated before anything is stored; every imported entity gets a fresh id (folder references are
/// remapped), so an import can never overwrite existing data. Credentials, transport settings, scripts and account fields are
/// never imported.
/// </summary>
public sealed class CollectionJsonService
{
    public const int MaxFileBytes = 10 * 1024 * 1024;
    public const int SupportedSchemaVersion = 1;
    private const int MaxDepth = 64;

    private static readonly JsonSerializerOptions ExportOptions = new(StorageJson.Options) { WriteIndented = true };

    // ------------------------------------------------------------------ export

    /// <summary>Serializes the collection (secret-free by construction; run history is not part of it).</summary>
    public string Export(CollectionDefinition collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var envelope = new Dictionary<string, object>
        {
            ["schemaVersion"] = SupportedSchemaVersion,
            ["kind"] = "collection",
            ["definition"] = collection with { SchemaVersion = CollectionDefinition.CurrentSchemaVersion },
        };
        return JsonSerializer.Serialize(envelope, ExportOptions);
    }

    public Task ExportFileAsync(CollectionDefinition collection, string path, CancellationToken cancellationToken) =>
        Task.Run(() => AtomicFile.WriteAllText(path, Export(collection)), cancellationToken);

    // ------------------------------------------------------------------ import

    public async Task<ImportOutcome<CollectionDefinition>> ImportFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return ImportOutcome<CollectionDefinition>.Failed([$"The file “{path}” does not exist."], []);
            if (info.Length > MaxFileBytes)
                return ImportOutcome<CollectionDefinition>.Failed([$"The file is {info.Length / (1024.0 * 1024):N1} MiB; imports are limited to {MaxFileBytes / (1024 * 1024)} MiB."], []);

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return Import(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ImportOutcome<CollectionDefinition>.Failed([$"The file could not be read ({ex.GetType().Name})."], []);
        }
    }

    public ImportOutcome<CollectionDefinition> Import(byte[] utf8)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (utf8.Length > MaxFileBytes)
            return ImportOutcome<CollectionDefinition>.Failed([$"The file is larger than {MaxFileBytes / (1024 * 1024)} MiB."], warnings);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(Utf8Bom.Strip(utf8), new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line && ex.BytePositionInLine is { } pos ? $" (line {line + 1}, position {pos + 1})" : string.Empty;
            return ImportOutcome<CollectionDefinition>.Failed([$"The file is not valid JSON{where}, or is nested deeper than {MaxDepth} levels."], warnings);
        }

        using (document)
        {
            var envelope = FieldReader.Open(document.RootElement, "file", errors, warnings);
            if (envelope is null)
                return ImportOutcome<CollectionDefinition>.Failed(errors, warnings);

            var version = envelope.Int("schemaVersion", required: true);
            var kind = envelope.String("kind", required: true, 32);
            var definitionElement = envelope.Object("definition", required: true);
            envelope.ReportUnknown();

            if (version is not null && version != SupportedSchemaVersion)
                errors.Add($"file.schemaVersion: version {version} is not supported (this restByAno reads version {SupportedSchemaVersion}).");
            if (kind is not null && kind != "collection")
                errors.Add($"file.kind: this file is “{FieldReader.Trim(kind)}”, not a collection.");
            if (errors.Count > 0 || definitionElement is null)
                return ImportOutcome<CollectionDefinition>.Failed(errors, warnings);

            var collection = ReadCollection(definitionElement.Value, errors, warnings);
            return errors.Count > 0 || collection is null
                ? ImportOutcome<CollectionDefinition>.Failed(errors, warnings)
                : new ImportOutcome<CollectionDefinition>(collection, [], warnings, []);
        }
    }

    private static CollectionDefinition? ReadCollection(JsonElement element, List<string> errors, List<string> warnings)
    {
        var r = FieldReader.Open(element, "definition", errors, warnings);
        if (r is null) return null;

        r.Guid("id", required: false); // read for the report only; a fresh id is always allocated
        var name = r.String("name", required: true, 200);
        if (name is not null && string.IsNullOrWhiteSpace(name))
            errors.Add("definition.name: the collection needs a name.");
        var schema = r.Int("schemaVersion", required: false);
        if (schema is not null && schema != CollectionDefinition.CurrentSchemaVersion)
            errors.Add($"definition.schemaVersion: version {schema} is not supported.");

        ForbidUnsafeFields(r);

        var defaults = ReadVariables(r.Array("defaults", required: false), "definition.defaults", errors, warnings);

        // Folders
        var folderMap = new Dictionary<Guid, Guid>();
        var folders = new List<FolderDefinition>();
        if (r.Array("folders", required: false) is { } folderList)
        {
            var index = 0;
            foreach (var item in folderList.EnumerateArray())
            {
                var path = FieldReader.Index("definition.folders", index);
                var f = FieldReader.Open(item, path, errors, warnings);
                index++;
                if (f is null) continue;
                var oldId = f.Guid("id", required: true);
                var folderName = f.String("name", required: true, 200);
                var order = f.Int("order", required: false) ?? index - 1;
                f.ReportUnknown();
                if (oldId is null || folderName is null) continue;
                if (string.IsNullOrWhiteSpace(folderName)) errors.Add($"{path}.name: the folder needs a name.");
                if (!folderMap.TryAdd(oldId.Value, Guid.NewGuid()))
                {
                    errors.Add($"{path}.id: the folder id is used more than once.");
                    continue;
                }

                folders.Add(new FolderDefinition { Id = folderMap[oldId.Value], Name = folderName.Trim(), Order = order });
            }
        }

        // Requests
        var requests = new List<RequestDefinition>();
        var seenRequestIds = new HashSet<Guid>();
        if (r.Array("requests", required: false) is { } requestList)
        {
            var index = 0;
            foreach (var item in requestList.EnumerateArray())
            {
                var request = ReadRequest(item, FieldReader.Index("definition.requests", index), index, folderMap, seenRequestIds, errors, warnings);
                index++;
                if (request is not null) requests.Add(request);
            }
        }

        r.ReportUnknown();
        if (name is null) return null;

        return new CollectionDefinition
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Defaults = defaults,
            Folders = folders,
            Requests = requests,
        };
    }

    private static void ForbidUnsafeFields(FieldReader r)
    {
        foreach (var field in new[] { "script", "scripts", "preRequestScript", "postResponseScript", "tests", "events" })
            r.Refuse(field, "scripts are never imported or executed.");
        foreach (var field in new[] { "workspace", "workspaceId", "team", "teamId", "owner", "account", "cloud", "sync", "shared", "uid" })
            r.Refuse(field, "cloud/account information is never imported.");
        r.Refuse("history", "run history is not part of an import.");
        r.Refuse("transportProfile", "transport and proxy settings are never imported.");
    }

    private static RequestDefinition? ReadRequest(
        JsonElement element, string path, int index, Dictionary<Guid, Guid> folderMap, HashSet<Guid> seenIds,
        List<string> errors, List<string> warnings)
    {
        var r = FieldReader.Open(element, path, errors, warnings);
        if (r is null) return null;

        var before = errors.Count;
        var id = r.Guid("id", required: false);
        if (id is not null && !seenIds.Add(id.Value))
            errors.Add($"{path}.id: the request id is used more than once.");

        Guid? folderId = null;
        if (r.Guid("folderId", required: false) is { } oldFolder)
        {
            if (folderMap.TryGetValue(oldFolder, out var mapped)) folderId = mapped;
            else errors.Add($"{path}.folderId: refers to a folder that is not in this file.");
        }

        var order = r.Int("order", required: false) ?? index;
        var name = r.String("name", required: true, 200);
        if (name is not null && string.IsNullOrWhiteSpace(name))
            errors.Add($"{path}.name: the request needs a name.");
        var method = r.Enum<HttpMethodKind>("method", required: true);
        var url = r.String("urlTemplate", required: false, 32_768) ?? string.Empty;
        var query = ReadPairs(r.Array("queryParameters", required: false), $"{path}.queryParameters", errors, warnings, isHeader: false);
        var headers = ReadPairs(r.Array("headers", required: false), $"{path}.headers", errors, warnings, isHeader: true);
        var body = ReadBody(r.Object("body", required: false), $"{path}.body", name, errors, warnings);
        var auth = ReadAuth(r.Object("auth", required: false), $"{path}.auth", errors, warnings);
        var assertions = ReadAssertions(r.Array("assertions", required: false), $"{path}.assertions", errors, warnings);
        var extractors = ReadExtractors(r.Array("extractors", required: false), $"{path}.extractors", errors, warnings);
        var cleanup = r.Bool("isCleanup") ?? false;
        ForbidUnsafeFields(r);
        r.Refuse("resolvedUrl", "resolved request values are never imported.");
        r.Refuse("resolvedHeaders", "resolved request values are never imported.");
        r.ReportUnknown();

        if (errors.Count > before || name is null || method is null)
            return null;

        return new RequestDefinition
        {
            Id = Guid.NewGuid(),
            FolderId = folderId,
            Order = order,
            Name = name.Trim(),
            Method = method.Value,
            UrlTemplate = url,
            QueryParameters = query,
            Headers = headers,
            Body = body,
            Auth = auth,
            Assertions = assertions,
            Extractors = extractors,
            IsCleanup = cleanup,
        };
    }

    private static IReadOnlyList<KeyValueEntry> ReadPairs(JsonElement? list, string path, List<string> errors, List<string> warnings, bool isHeader)
    {
        var result = new List<KeyValueEntry>();
        if (list is null) return result;
        var index = 0;
        foreach (var item in list.Value.EnumerateArray())
        {
            var itemPath = FieldReader.Index(path, index++);
            var p = FieldReader.Open(item, itemPath, errors, warnings);
            if (p is null) continue;
            var name = p.String("name", required: true, 1024);
            var value = p.String("value", required: false, 100_000) ?? string.Empty;
            var enabled = p.Bool("enabled") ?? true;
            p.ReportUnknown();
            if (name is null) continue;
            if (isHeader && (name.Contains('\r') || name.Contains('\n') || value.Contains('\r') || value.Contains('\n')))
            {
                errors.Add($"{itemPath}: header names and values cannot contain line breaks.");
                continue;
            }

            result.Add(new KeyValueEntry { Name = name, Value = value, Enabled = enabled });
        }

        return result;
    }

    private static BodyDefinition ReadBody(JsonElement? element, string path, string? requestName, List<string> errors, List<string> warnings)
    {
        if (element is null) return new BodyDefinition();
        var b = FieldReader.Open(element.Value, path, errors, warnings);
        if (b is null) return new BodyDefinition();

        var kind = b.Enum<BodyKind>("kind", required: true) ?? BodyKind.None;
        var content = b.String("content", required: false, 8 * 1024 * 1024) ?? string.Empty;
        var contentType = b.String("contentType", required: false, 256);
        b.ReportUnknown();

        if (contentType is not null && (contentType.Contains('\r') || contentType.Contains('\n')))
            errors.Add($"{path}.contentType: line breaks are not allowed.");
        if (kind == BodyKind.Json)
        {
            var jsonError = RequestBuilder.JsonSyntaxError(content);
            if (jsonError is not null)
                errors.Add($"{path}.content: {jsonError}");
        }

        return kind == BodyKind.None
            ? new BodyDefinition()
            : new BodyDefinition { Kind = kind, Content = content, ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType };
    }

    private static AuthDefinition ReadAuth(JsonElement? element, string path, List<string> errors, List<string> warnings)
    {
        if (element is null) return new AuthDefinition();
        var a = FieldReader.Open(element.Value, path, errors, warnings);
        if (a is null) return new AuthDefinition();

        var kind = a.Enum<AuthKind>("kind", required: true) ?? AuthKind.None;
        var token = a.String("tokenVariable", required: false, 128);
        var user = a.String("usernameVariable", required: false, 128);
        var password = a.String("passwordVariable", required: false, 128);
        var keyName = a.String("apiKeyName", required: false, 256);
        var keyVariable = a.String("apiKeyVariable", required: false, 128);
        var location = a.Enum<ApiKeyLocation>("apiKeyLocation", required: false);

        // Anything else under auth could be a literal credential: refuse the whole file instead of ignoring it.
        foreach (var field in new[] { "token", "password", "username", "user", "apiKey", "key", "secret", "value", "credentials", "bearer", "basic" })
            a.Forbid(field, "authentication must only reference variable names; credential values are never imported.");
        a.ReportUnknown();

        void Need(string? value, string field)
        {
            if (!VariableNames.IsValid(value))
                errors.Add($"{path}.{field}: a valid variable name is required for {kind} authentication.");
        }

        switch (kind)
        {
            case AuthKind.Bearer:
                Need(token, "tokenVariable");
                break;
            case AuthKind.Basic:
                Need(user, "usernameVariable");
                Need(password, "passwordVariable");
                break;
            case AuthKind.ApiKey:
                if (string.IsNullOrWhiteSpace(keyName)) errors.Add($"{path}.apiKeyName: required for API key authentication.");
                Need(keyVariable, "apiKeyVariable");
                if (location is null) errors.Add($"{path}.apiKeyLocation: required (Header or Query).");
                break;
        }

        return kind switch
        {
            AuthKind.Bearer => new AuthDefinition { Kind = kind, TokenVariable = token },
            AuthKind.Basic => new AuthDefinition { Kind = kind, UsernameVariable = user, PasswordVariable = password },
            AuthKind.ApiKey => new AuthDefinition { Kind = kind, ApiKeyName = keyName?.Trim(), ApiKeyVariable = keyVariable, ApiKeyLocation = location },
            _ => new AuthDefinition(),
        };
    }

    private static IReadOnlyList<AssertionDefinition> ReadAssertions(JsonElement? list, string path, List<string> errors, List<string> warnings)
    {
        var result = new List<AssertionDefinition>();
        if (list is null) return result;
        var seen = new HashSet<Guid>();
        var index = 0;
        foreach (var item in list.Value.EnumerateArray())
        {
            var itemPath = FieldReader.Index(path, index++);
            var a = FieldReader.Open(item, itemPath, errors, warnings);
            if (a is null) continue;
            var before = errors.Count;
            var id = a.Guid("id", required: false);
            if (id is not null && !seen.Add(id.Value)) errors.Add($"{itemPath}.id: the assertion id is used more than once.");
            var kind = a.Enum<AssertionKind>("kind", required: true);
            var header = a.String("headerName", required: false, 256);
            var pointer = a.String("jsonPointer", required: false, 4096);
            var expectedJson = a.String("expectedJson", required: false, 100_000);
            var status = a.Int("expectedStatus", required: false);
            var max = a.Int("maxMilliseconds", required: false);
            a.ReportUnknown();
            if (kind is null || errors.Count > before) continue;

            switch (kind.Value)
            {
                case AssertionKind.StatusEquals:
                    if (status is null or < 100 or > 599) errors.Add($"{itemPath}.expectedStatus: a status code from 100 to 599 is required.");
                    break;
                case AssertionKind.HeaderEquals:
                    if (string.IsNullOrWhiteSpace(header)) errors.Add($"{itemPath}.headerName: required.");
                    if (!IsJsonScalar(expectedJson, stringOnly: true)) errors.Add($"{itemPath}.expectedJson: the expected header value must be a JSON string, e.g. \"application/json\".");
                    break;
                case AssertionKind.JsonExists:
                    if (!JsonPointerNavigator.IsValid(pointer)) errors.Add($"{itemPath}.jsonPointer: a valid JSON pointer is required.");
                    break;
                case AssertionKind.JsonEquals:
                    if (!JsonPointerNavigator.IsValid(pointer)) errors.Add($"{itemPath}.jsonPointer: a valid JSON pointer is required.");
                    if (!IsJsonScalar(expectedJson, stringOnly: false)) errors.Add($"{itemPath}.expectedJson: a JSON string, number, true, false or null is required (objects and lists are not supported).");
                    break;
                case AssertionKind.DurationBelow:
                    if (max is null or < 1) errors.Add($"{itemPath}.maxMilliseconds: a positive number is required.");
                    break;
            }

            if (errors.Count > before) continue;
            result.Add(new AssertionDefinition
            {
                Id = Guid.NewGuid(),
                Kind = kind.Value,
                HeaderName = kind == AssertionKind.HeaderEquals ? header!.Trim() : null,
                JsonPointer = kind is AssertionKind.JsonExists or AssertionKind.JsonEquals ? pointer : null,
                ExpectedJson = kind is AssertionKind.HeaderEquals or AssertionKind.JsonEquals ? expectedJson : null,
                ExpectedStatus = kind == AssertionKind.StatusEquals ? status : null,
                MaxMilliseconds = kind == AssertionKind.DurationBelow ? max : null,
            });
        }

        return result;
    }

    private static IReadOnlyList<ExtractorDefinition> ReadExtractors(JsonElement? list, string path, List<string> errors, List<string> warnings)
    {
        var result = new List<ExtractorDefinition>();
        if (list is null) return result;
        var seen = new HashSet<Guid>();
        var index = 0;
        foreach (var item in list.Value.EnumerateArray())
        {
            var itemPath = FieldReader.Index(path, index++);
            var x = FieldReader.Open(item, itemPath, errors, warnings);
            if (x is null) continue;
            var before = errors.Count;
            var id = x.Guid("id", required: false);
            if (id is not null && !seen.Add(id.Value)) errors.Add($"{itemPath}.id: the extractor id is used more than once.");
            var pointer = x.String("jsonPointer", required: true, 4096);
            var variable = x.String("variableName", required: true, 128);
            var sensitive = x.Bool("sensitive");
            x.ReportUnknown();
            if (pointer is not null && !JsonPointerNavigator.IsValid(pointer)) errors.Add($"{itemPath}.jsonPointer: not a valid JSON pointer.");
            if (variable is not null && !VariableNames.IsValid(variable)) errors.Add($"{itemPath}.variableName: not a valid variable name.");
            if (sensitive == false) warnings.Add($"{itemPath}.sensitive: extracted values are always treated as sensitive in this version.");
            if (errors.Count > before || pointer is null || variable is null) continue;
            result.Add(new ExtractorDefinition { Id = Guid.NewGuid(), JsonPointer = pointer, VariableName = variable, Sensitive = true });
        }

        return result;
    }

    internal static IReadOnlyList<VariableDefinition> ReadVariables(JsonElement? list, string path, List<string> errors, List<string> warnings)
    {
        var result = new List<VariableDefinition>();
        if (list is null) return result;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in list.Value.EnumerateArray())
        {
            var itemPath = FieldReader.Index(path, index++);
            var v = FieldReader.Open(item, itemPath, errors, warnings);
            if (v is null) continue;
            var name = v.String("name", required: true, 128);
            var value = v.String("value", required: false, 1_000_000);
            v.Forbid("secretId", "secrets are never imported.");
            v.Forbid("requiresSecret", "collection defaults cannot be secret; use an environment for credentials.");
            v.Forbid("ciphertext", "secrets are never imported.");
            v.ReportUnknown();
            if (name is null) continue;
            if (!VariableNames.IsValid(name)) { errors.Add($"{itemPath}.name: “{FieldReader.Trim(name)}” is not a valid variable name."); continue; }
            if (!names.Add(name)) { errors.Add($"{itemPath}.name: the variable “{name}” is defined more than once."); continue; }
            result.Add(new VariableDefinition { Name = name, Value = value ?? string.Empty });
        }

        return result;
    }

    internal static bool IsJsonScalar(string? json, bool stringOnly)
    {
        if (json is null) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var kind = doc.RootElement.ValueKind;
            return stringOnly
                ? kind == JsonValueKind.String
                : kind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

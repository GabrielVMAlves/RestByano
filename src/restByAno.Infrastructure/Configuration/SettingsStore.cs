using System.Text.Json;
using restByAno.Infrastructure.Persistence;

namespace restByAno.Infrastructure.Configuration;

public sealed record SettingsLoadResult(bool Exists, AppSettings? Settings, StorageProblem? Problem);

/// <summary>Reads and atomically writes restByAno.settings.json. A damaged or newer file is reported, never overwritten.</summary>
public sealed class SettingsStore(StorageOptions options)
{
    private static readonly JsonSerializerOptions WriteOptions = new(StorageJson.Options) { WriteIndented = true };

    public SettingsLoadResult Load()
    {
        var path = options.SettingsPath;
        if (!File.Exists(path))
            return new SettingsLoadResult(false, null, null);

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, StorageJson.Options)
                           ?? throw new JsonException("The settings file is empty.");

            if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
                return Problem(StorageProblemKind.SettingsNewer, "Settings were saved by a newer restByAno",
                    $"The settings file '{path}' uses format version {settings.SchemaVersion}, but this version of restByAno understands up to {AppSettings.CurrentSchemaVersion}. The file was not changed. Install a newer restByAno, or start with --data-dir pointing at a different folder.");

            var errors = settings.Validate();
            if (errors.Count > 0)
                return Problem(StorageProblemKind.SettingsInvalid, "Settings contain invalid values",
                    $"The settings file '{path}' has invalid values: {string.Join(" ", errors)} Correct the file in a text editor and start restByAno again. The file was not changed.");

            return new SettingsLoadResult(true, settings, null);
        }
        catch (JsonException)
        {
            return Problem(StorageProblemKind.SettingsCorrupt, "Settings file is damaged",
                $"The settings file '{path}' is not valid JSON. The file was not changed. Fix or move it aside in a text editor and start restByAno again; your database is untouched.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Problem(StorageProblemKind.Unwritable, "Settings file cannot be read",
                $"The settings file '{path}' could not be read ({ex.GetType().Name}). Check that the file is not locked and that you have access to the folder.");
        }
    }

    /// <summary>Writes through a temporary sibling file and replaces the target atomically.</summary>
    public void Save(AppSettings settings)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(settings));

        var path = options.SettingsPath;
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, WriteOptions));
            if (File.Exists(path))
                File.Replace(temp, path, destinationBackupFileName: null);
            else
                File.Move(temp, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(new StorageProblem(StorageProblemKind.Unwritable, "Settings cannot be saved",
                $"The settings file '{path}' could not be written ({ex.GetType().Name}). Check that the folder is writable and the file is not locked. Existing settings were not changed."), ex);
        }
        finally
        {
            TryDeleteOwnTemp(temp);
        }
    }

    private static void TryDeleteOwnTemp(string temp)
    {
        try
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: only this app's own temp file; leftovers are cleaned on the next start.
        }
    }

    private static SettingsLoadResult Problem(StorageProblemKind kind, string title, string message) =>
        new(true, null, new StorageProblem(kind, title, message));
}

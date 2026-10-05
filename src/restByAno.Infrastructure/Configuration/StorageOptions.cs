using System.Security.Cryptography;
using System.Text;

namespace restByAno.Infrastructure.Configuration;

/// <summary>
/// Where restByAno keeps its files. The data directory is C:\temp unless the app was started with
/// <c>--data-dir &lt;absolute path&gt;</c>; there is deliberately no registry or roaming pointer.
/// </summary>
public sealed class StorageOptions
{
    public const string DefaultDataDirectory = @"C:\temp";
    public const string SettingsFileName = "restByAno.settings.json";
    public const string DatabaseFileName = "restByAno.db";
    public const string BackupDirectoryName = "restByAno-backups";

    public StorageOptions(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Path.IsPathFullyQualified(dataDirectory))
            throw new ArgumentException("The data directory must be an absolute path.", nameof(dataDirectory));

        var full = Path.GetFullPath(dataDirectory);
        var root = Path.GetPathRoot(full);
        DataDirectory = full.Length > root!.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }

    public string DataDirectory { get; }
    public string SettingsPath => Path.Combine(DataDirectory, SettingsFileName);
    public string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);
    public string BackupDirectory => Path.Combine(DataDirectory, BackupDirectoryName);

    /// <summary>Stable key for the canonical directory (case-insensitive on Windows), used for the single-instance mutex.</summary>
    public string CanonicalKey
    {
        get
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(DataDirectory.ToUpperInvariant()));
            return Convert.ToHexString(bytes, 0, 8);
        }
    }

    public static StorageOptions Default() => new(DefaultDataDirectory);

    /// <summary>Parses <c>--data-dir &lt;path&gt;</c> or <c>--data-dir=&lt;path&gt;</c>. Other arguments are ignored.</summary>
    public static bool TryParse(IReadOnlyList<string> args, out StorageOptions? options, out string? error)
    {
        options = null;
        error = null;
        string? value = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string? candidate;
            if (arg.Equals("--data-dir", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    error = "--data-dir needs a value: an absolute folder path.";
                    return false;
                }

                candidate = args[++i];
            }
            else if (arg.StartsWith("--data-dir=", StringComparison.OrdinalIgnoreCase))
            {
                candidate = arg["--data-dir=".Length..];
            }
            else
            {
                continue;
            }

            if (value is not null)
            {
                error = "--data-dir was given more than once.";
                return false;
            }

            value = candidate;
        }

        if (value is null)
        {
            options = Default();
            return true;
        }

        value = value.Trim().Trim('"');
        if (value.Length == 0 || !Path.IsPathFullyQualified(value) || value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = $"--data-dir must be an absolute folder path, but was '{value}'.";
            return false;
        }

        options = new StorageOptions(value);
        return true;
    }
}

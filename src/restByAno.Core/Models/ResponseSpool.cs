namespace restByAno.Core.Models;

/// <summary>
/// Large response bodies are not held in memory: the executor keeps a leading sample and streams the whole decoded body to a
/// temporary file. This type owns that file's location and lifetime and loads it back on request.
/// </summary>
public static class ResponseSpool
{
    /// <summary>Bodies up to this size stay in memory; larger ones keep only this many leading bytes as the sample.</summary>
    public const int SampleBytes = 1024 * 1024;

    /// <summary>Largest body the UI loads into its text boxes ("Load full body"). Larger bodies can only be saved to a file.</summary>
    public const long MaxDisplayBytes = 32L * 1024 * 1024;

    /// <summary>Largest body that JSON assertions and extractors parse. Larger bodies make those rules fail with a readable reason.</summary>
    public const long MaxEvaluationBytes = 256L * 1024 * 1024;

    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(1);

    public static string Directory { get; } = Path.Combine(Path.GetTempPath(), "restByAno", "responses");

    /// <summary>Creates the spool directory if needed and returns a new, unused file path in it.</summary>
    public static string NewPath()
    {
        System.IO.Directory.CreateDirectory(Directory);
        return Path.Combine(Directory, $"{Guid.NewGuid():N}.body");
    }

    public static void Delete(string? path)
    {
        if (path is null) return;
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: leftovers are removed by CleanupStale on a later start.
        }
    }

    /// <summary>Removes spool files left behind by a crashed or killed run.</summary>
    public static void CleanupStale()
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return;
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.body"))
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > StaleAfter)
                    Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    /// <summary>
    /// Returns the response with its complete body in memory. A response that is already complete is returned as is.
    /// Returns null when the spool file is gone, unreadable, or larger than <paramref name="maxBytes"/>.
    /// </summary>
    public static ResponseSnapshot? TryLoadFull(ResponseSnapshot response, long maxBytes)
    {
        if (response.SpoolPath is not { } path)
            return response;
        if (response.BodyLength > maxBytes || response.BodyLength > Array.MaxLength)
            return null;

        try
        {
            return response with { Body = File.ReadAllBytes(path), SpoolPath = null, TotalBodyBytes = null };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            return null;
        }
    }
}

namespace restByAno.Infrastructure.Configuration;

/// <summary>
/// Holds a named mutex for the canonical data directory for the lifetime of the app, so two instances never
/// share one database. Acquire and release on the same (UI) thread.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _released;

    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    /// <summary>Returns the guard, or null when another instance already uses this data directory.</summary>
    public static SingleInstanceGuard? TryAcquire(StorageOptions options)
    {
        var mutex = new Mutex(initiallyOwned: false, name: $@"Local\restByAno-{options.CanonicalKey}");
        try
        {
            if (mutex.WaitOne(0))
                return new SingleInstanceGuard(mutex);
        }
        catch (AbandonedMutexException)
        {
            // A previous instance crashed while holding it; we now own it.
            return new SingleInstanceGuard(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_released) return;
        _released = true;
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from another thread; closing the handle below still frees the name.
        }

        _mutex.Dispose();
    }
}

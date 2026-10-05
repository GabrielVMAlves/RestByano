using restByAno.Infrastructure.Configuration;

namespace restByAno.Desktop.Services;

/// <summary>Current settings for the running app; every change is validated and written atomically.</summary>
public sealed class SettingsService(SettingsStore store)
{
    private AppSettings _current = new();

    public AppSettings Current => _current;

    public event EventHandler? Changed;

    public void Initialize(AppSettings settings) => _current = settings;

    /// <summary>Applies a change and saves it. Throws <see cref="restByAno.Infrastructure.Persistence.StorageException"/> if the file cannot be written; the in-memory value is then unchanged.</summary>
    public void Update(Func<AppSettings, AppSettings> change)
    {
        var next = change(_current);
        if (next == _current) return;
        store.Save(next);
        _current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

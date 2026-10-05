using System.Windows;
using restByAno.Core.Contracts;
using restByAno.Core.Models;
using restByAno.Core.Resolution;
using restByAno.Core.Running;
using restByAno.Desktop.Services;
using restByAno.Desktop.ViewModels;
using restByAno.Desktop.Views;
using restByAno.Infrastructure.Configuration;
using restByAno.Infrastructure.Http;
using restByAno.Infrastructure.ImportExport;
using restByAno.Infrastructure.Persistence;
using restByAno.Infrastructure.Policy;
using restByAno.Infrastructure.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace restByAno.Desktop;

public partial class App : Application
{
    private ServiceProvider? _services;
    private SingleInstanceGuard? _instanceGuard;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Startup dialogs may be the only windows for a while; the app exits explicitly.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var theme = new ThemeService();
        theme.Start();
        var dialogs = new DialogService();

        if (!StorageOptions.TryParse(e.Args, out var options, out var argumentError))
        {
            dialogs.ShowMessage("restByAno cannot start", argumentError!);
            Shutdown(2);
            return;
        }

        // One instance per canonical data directory, held for the lifetime of the app.
        _instanceGuard = SingleInstanceGuard.TryAcquire(options!);
        if (_instanceGuard is null)
        {
            dialogs.ShowMessage(
                "restByAno is already running",
                "Another restByAno is using this data folder, and two instances must not share one database. Switch to the running window, or start this one with --data-dir <another absolute folder>.",
                options!.DataDirectory);
            Shutdown(1);
            return;
        }

        // Composition root: the only place services are wired and resolved.
        var collection = new ServiceCollection();
        ConfigureServices(collection, options!, theme, dialogs);
        _services = collection.BuildServiceProvider();

        var settings = _services.GetRequiredService<StartupCoordinator>().Run();
        if (settings is null)
        {
            Shutdown(3);
            return;
        }

        _services.GetRequiredService<SettingsService>().Initialize(settings);

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services, StorageOptions options, ThemeService theme, IDialogService dialogs)
    {
        services.AddSingleton(theme);
        services.AddSingleton(dialogs);

        // Storage: data directory comes from --data-dir or C:\temp.
        services.AddSingleton(options);
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<DatabaseInitializer>(sp => new DatabaseInitializer(sp.GetRequiredService<StorageOptions>()));
        services.AddSingleton<StorageBootstrapper>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<ICollectionRepository, CollectionRepository>();
        services.AddSingleton<IEnvironmentRepository, EnvironmentRepository>();
        services.AddSingleton<ISecretStore, DpapiSecretStore>();
        services.AddSingleton<StartupCoordinator>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<IWindowLauncher, WindowLauncher>();
        services.AddSingleton(sp => new VariableSnapshotFactory(sp.GetService<ISecretStore>()));

        // Destination allow-list: managed policy file, or loopback only when none is installed.
        services.AddSingleton<ManagedTargetPolicy>();
        services.AddSingleton<ITargetPolicy>(sp => sp.GetRequiredService<ManagedTargetPolicy>());
        services.AddSingleton<HttpClientPool>();
        services.AddSingleton<IRequestExecutor, HttpRequestExecutor>();

        services.AddSingleton<IClipboardService, WpfClipboardService>();
        services.AddSingleton<ResponseViewModel>();
        services.AddSingleton<RequestEditorViewModel>();
        services.AddSingleton<ActiveEnvironmentViewModel>();
        services.AddSingleton<RunState>();
        services.AddSingleton<CollectionRunner>();
        services.AddSingleton<RunnerViewModel>();
        services.AddSingleton<JsonReportExporter>();
        services.AddSingleton<IRunRepository, RunRepository>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<CollectionTreeViewModel>();
        services.AddSingleton<CollectionJsonService>();
        services.AddSingleton<EnvironmentJsonService>();
        services.AddTransient<EnvironmentEditorViewModel>();
        services.AddSingleton<Func<EnvironmentEditorViewModel>>(sp => () => sp.GetRequiredService<EnvironmentEditorViewModel>());
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<Func<SettingsViewModel>>(sp => () => sp.GetRequiredService<SettingsViewModel>());
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }
}

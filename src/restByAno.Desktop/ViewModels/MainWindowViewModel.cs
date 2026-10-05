using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using restByAno.Desktop.Services;

namespace restByAno.Desktop.ViewModels;

public sealed partial class MainWindowViewModel(
    RequestEditorViewModel requestEditor,
    CollectionTreeViewModel collectionTree,
    ActiveEnvironmentViewModel activeEnvironment,
    RunnerViewModel runner,
    HistoryViewModel history,
    IWindowLauncher windows,
    Func<SettingsViewModel> createSettings,
    Func<EnvironmentEditorViewModel> createEnvironmentEditor) : ObservableObject
{
    public string Title => "restByAno";

    public RequestEditorViewModel RequestEditor { get; } = requestEditor;

    public CollectionTreeViewModel CollectionTree { get; } = collectionTree;

    public ActiveEnvironmentViewModel ActiveEnvironment { get; } = activeEnvironment;

    public RunnerViewModel Runner { get; } = runner;

    public HistoryViewModel History { get; } = history;

    public ResponseViewModel Response => RequestEditor.Response;

    public async Task InitializeAsync()
    {
        await ActiveEnvironment.ReloadAsync();
        await CollectionTree.InitializeAsync();
        await History.InitializeAsync();   // applies the retention purge, then lists runs
    }

    [RelayCommand]
    private void ShowSettings() => windows.ShowSettings(createSettings());

    [RelayCommand]
    private async Task ManageEnvironmentsAsync()
    {
        windows.ShowEnvironmentEditor(createEnvironmentEditor());
        await ActiveEnvironment.ReloadAsync();
    }
}

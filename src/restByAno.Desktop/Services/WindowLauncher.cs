using System.Windows;
using restByAno.Core.Models;
using restByAno.Desktop.ViewModels;
using restByAno.Desktop.Views;

namespace restByAno.Desktop.Services;

/// <summary>Opens the app's secondary windows, centred on the main window.</summary>
public interface IWindowLauncher
{
    void ShowEnvironmentEditor(EnvironmentEditorViewModel viewModel);

    void ShowSettings(SettingsViewModel viewModel);

    /// <summary>Returns the edited defaults, or null when cancelled.</summary>
    IReadOnlyList<VariableDefinition>? EditCollectionVariables(CollectionVariablesViewModel viewModel);
}

public sealed class WindowLauncher : IWindowLauncher
{
    public void ShowEnvironmentEditor(EnvironmentEditorViewModel viewModel)
    {
        var window = new EnvironmentEditorWindow(viewModel);
        Place(window);
        window.ShowDialog();
    }

    public void ShowSettings(SettingsViewModel viewModel)
    {
        var window = new SettingsWindow(viewModel);
        Place(window);
        window.ShowDialog();
    }

    public IReadOnlyList<VariableDefinition>? EditCollectionVariables(CollectionVariablesViewModel viewModel)
    {
        var window = new CollectionVariablesWindow(viewModel);
        Place(window);
        return window.ShowDialog() == true ? window.Result : null;
    }

    private static void Place(Window window)
    {
        if (Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
}

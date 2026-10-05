using System.Windows;
using restByAno.Desktop.Services;
using restByAno.Desktop.ViewModels;

namespace restByAno.Desktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        TransportHint.Text = viewModel.HasActiveEnvironment
            ? $"These settings belong to the environment “{viewModel.ActiveEnvironmentName}” and apply to requests sent while it is selected."
            : "Select an environment in the toolbar to edit its transport settings. Without an environment the system proxy is used and plain HTTP to non-loopback hosts is off.";
        DarkTitleBar.Apply(this);
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}

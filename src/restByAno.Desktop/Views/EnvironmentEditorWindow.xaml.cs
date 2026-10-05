using System.ComponentModel;
using System.Windows;
using restByAno.Desktop.Services;
using restByAno.Desktop.ViewModels;

namespace restByAno.Desktop.Views;

public partial class EnvironmentEditorWindow : Window
{
    private readonly EnvironmentEditorViewModel _viewModel;
    private bool _closeConfirmed;

    public EnvironmentEditorWindow(EnvironmentEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        DarkTitleBar.Apply(this);
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }

    private bool _reverting;

    /// <summary>The user picked an item: ask the view model first and put the old item back if they cancel.</summary>
    private async void OnEnvironmentSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_reverting) return;
        var picked = EnvironmentList.SelectedItem as EnvironmentListItem;
        if (ReferenceEquals(picked, _viewModel.Selected)) return;

        if (!await _viewModel.TrySelectAsync(picked))
        {
            _reverting = true;
            EnvironmentList.SelectedItem = _viewModel.Selected;
            _reverting = false;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.Cancel || !_viewModel.IsDirty)
            return;

        e.Cancel = true;
        if (await _viewModel.ConfirmLeaveAsync())
        {
            _closeConfirmed = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }
}

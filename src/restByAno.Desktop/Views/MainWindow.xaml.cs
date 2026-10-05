using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using restByAno.Desktop.Services;
using restByAno.Desktop.ViewModels;

namespace restByAno.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private bool _closeConfirmed;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Default 1280x800, but never larger than the usable screen area.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);

        DarkTitleBar.Apply(this);
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }

    private void OnCloseExecuted(object sender, ExecutedRoutedEventArgs e) => Close();

    /// <summary>Asks Save / Discard / Cancel about unsaved request edits before the window really closes.</summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.Cancel)
            return;

        if (_viewModel.Runner.IsRunning)
        {
            var stop = new Services.DialogService().Show(new Services.DialogRequest(
                "A run is in progress",
                "A collection run is still going. Stop it and exit? The request in flight is cancelled and no cleanup is sent.",
                [new Services.DialogButton("Stop run and exit"), new Services.DialogButton("Keep running", IsDefault: true, IsCancel: true)]));
            if (stop != 0)
            {
                e.Cancel = true;
                return;
            }

            _viewModel.Runner.CancelRun();
        }

        if (!_viewModel.RequestEditor.IsDirty)
            return;

        e.Cancel = true; // the confirmation may await a save; close again afterwards
        if (await _viewModel.CollectionTree.ConfirmLeaveAsync())
        {
            _closeConfirmed = true;
            // Window.Close() is not allowed while this closing event is still being processed, so defer it.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }
}

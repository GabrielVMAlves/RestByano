using System.Windows;
using restByAno.Core.Models;
using restByAno.Desktop.Services;
using restByAno.Desktop.ViewModels;

namespace restByAno.Desktop.Views;

public partial class CollectionVariablesWindow : Window
{
    private readonly CollectionVariablesViewModel _viewModel;

    public CollectionVariablesWindow(CollectionVariablesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        HeaderText.Text = $"Variables of “{viewModel.CollectionName}”";
        DarkTitleBar.Apply(this);
    }

    /// <summary>The validated defaults after the user clicked Save; otherwise null.</summary>
    public IReadOnlyList<VariableDefinition>? Result { get; private set; }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        Result = _viewModel.TryAccept();
        if (Result is not null)
            DialogResult = true;
    }
}

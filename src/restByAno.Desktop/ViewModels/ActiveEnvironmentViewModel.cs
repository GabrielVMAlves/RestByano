using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using restByAno.Core.Contracts;
using restByAno.Core.Models;
using restByAno.Desktop.Services;
using restByAno.Infrastructure.Persistence;

namespace restByAno.Desktop.ViewModels;

public sealed record EnvironmentOption(Guid? Id, string Name);

/// <summary>The environment chosen in the toolbar. The choice is saved in settings; the environment itself is loaded fresh at each Send/Run.</summary>
public sealed partial class ActiveEnvironmentViewModel : ObservableObject
{
    private static readonly EnvironmentOption None = new(null, "No environment");

    private readonly IEnvironmentRepository _repository;
    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;
    private bool _loading;

    public ActiveEnvironmentViewModel(IEnvironmentRepository repository, SettingsService settings, IDialogService dialogs)
    {
        _repository = repository;
        _settings = settings;
        _dialogs = dialogs;
        Options.Add(None);
    }

    public ObservableCollection<EnvironmentOption> Options { get; } = [];

    [ObservableProperty]
    private EnvironmentOption _selected = None;

    public Guid? SelectedId => Selected.Id;

    /// <summary>Reloads the list from storage and restores the saved selection (or keeps the current one).</summary>
    public async Task ReloadAsync()
    {
        _loading = true;
        try
        {
            var wanted = Selected.Id ?? _settings.Current.SelectedEnvironmentId;
            var summaries = await _repository.ListAsync(default);

            Options.Clear();
            Options.Add(None);
            foreach (var summary in summaries)
                Options.Add(new EnvironmentOption(summary.Id, summary.Name));

            Selected = Options.FirstOrDefault(o => o.Id == wanted) ?? None;
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
        }
        finally
        {
            _loading = false;
        }

        Persist();
    }

    /// <summary>Loads the active environment from storage now, so later edits cannot change a send already in progress.</summary>
    public async Task<EnvironmentDefinition?> LoadActiveAsync(CancellationToken cancellationToken) =>
        Selected.Id is { } id ? await _repository.LoadAsync(id, cancellationToken) : null;

    partial void OnSelectedChanged(EnvironmentOption value)
    {
        OnPropertyChanged(nameof(SelectedId));
        if (!_loading) Persist();
    }

    private void Persist()
    {
        try
        {
            _settings.Update(s => s with { SelectedEnvironmentId = Selected.Id });
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
        }
    }
}

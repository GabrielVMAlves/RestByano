using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using restByAno.Core.Contracts;
using restByAno.Core.Models;
using restByAno.Desktop.Services;
using restByAno.Infrastructure.ImportExport;
using restByAno.Infrastructure.Persistence;

namespace restByAno.Desktop.ViewModels;

public sealed partial class EnvironmentListItem(Guid id, string name) : ObservableObject
{
    public Guid Id { get; } = id;

    [ObservableProperty]
    private string _name = name;
}

/// <summary>Add, select, rename and delete environments and edit their variables. Saved explicitly; unsaved edits are guarded.</summary>
public sealed partial class EnvironmentEditorViewModel : ObservableObject
{
    private readonly IEnvironmentRepository _repository;
    private readonly IDialogService _dialogs;
    private readonly EnvironmentJsonService _json;
    private EnvironmentListItem? _current;
    private EnvironmentDefinition? _loaded;
    private string _baseline = string.Empty;

    public EnvironmentEditorViewModel(IEnvironmentRepository repository, IDialogService dialogs, EnvironmentJsonService json)
    {
        _repository = repository;
        _dialogs = dialogs;
        _json = json;
        Variables.Changed += (_, _) => RecomputeDirty();
    }

    public ObservableCollection<EnvironmentListItem> Environments { get; } = [];

    public VariableTableViewModel Variables { get; } = new(allowSecrets: true);

    [ObservableProperty]
    private EnvironmentListItem? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    [ObservableProperty]
    private string _validationText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private string _selectedName = string.Empty;

    public bool HasSelection => _current is not null;

    public string StateText => _current is null ? string.Empty : IsDirty ? "● Unsaved changes" : "✔ Saved";

    public async Task InitializeAsync()
    {
        try
        {
            foreach (var summary in await _repository.ListAsync(default))
                Environments.Add(new EnvironmentListItem(summary.Id, summary.Name));
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
            return;
        }

        Selected = Environments.FirstOrDefault();
        await LoadAsync(Selected);
    }

    /// <summary>
    /// Called by the view when the user picks another environment. Returns false (and the view puts the old item
    /// back) when the user cancels the prompt about unsaved variable edits.
    /// </summary>
    public async Task<bool> TrySelectAsync(EnvironmentListItem? target)
    {
        if (ReferenceEquals(target, _current)) return true;
        if (!await ConfirmLeaveAsync()) return false;

        Selected = target;
        await LoadAsync(target);
        return true;
    }
    private async Task LoadAsync(EnvironmentListItem? item)
    {
        _current = item;
        _loaded = null;
        IReadOnlySet<Guid>? stored = null;
        if (item is not null)
        {
            try
            {
                _loaded = await _repository.LoadAsync(item.Id, default);
                stored = await _repository.GetStoredSecretIdsAsync(item.Id, default);
            }
            catch (StorageException ex)
            {
                _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
            }
        }

        Variables.Load(_loaded?.Variables ?? [], stored);
        SelectedName = item?.Name ?? string.Empty;
        _baseline = Fingerprint();
        ValidationText = string.Empty;
        RecomputeDirty();
        NotifyState();
    }

    private string Fingerprint() => string.Join("\u0001", Variables.Rows.Select(r => $"{r.Name}\u0002{r.Value}\u0002{r.IsSecret}\u0002{r.EntryVersion}"));

    private void RecomputeDirty() => IsDirty = _current is not null && Fingerprint() != _baseline;

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(StateText));
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>True when it is safe to switch environments or close (saved, discarded or nothing pending).</summary>
    public async Task<bool> ConfirmLeaveAsync()
    {
        if (!IsDirty || _current is null) return true;

        var choice = _dialogs.Show(new DialogRequest(
            "Unsaved changes",
            $"The variables of “{_current.Name}” have unsaved changes. Save them before continuing?",
            [new DialogButton("Save", IsDefault: true), new DialogButton("Discard"), new DialogButton("Cancel", IsCancel: true)]));

        switch (choice)
        {
            case 0:
                return await SaveCoreAsync();
            case 1:
                Variables.Load(_loaded?.Variables ?? []);
                _baseline = Fingerprint();
                RecomputeDirty();
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- commands

    [RelayCommand]
    private async Task NewEnvironmentAsync()
    {
        if (!await ConfirmLeaveAsync()) return;
        var name = _dialogs.PromptText("New environment", "Name for the new environment (for example Local or Staging):", "New environment", "Create");
        if (name is null) return;

        var definition = new EnvironmentDefinition { Id = Guid.NewGuid(), Name = name };
        try
        {
            await _repository.SaveAsync(definition, default);
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
            return;
        }

        var item = new EnvironmentListItem(definition.Id, name);
        Environments.Add(item);
        Selected = item;
        await LoadAsync(item);
    }

    // ---------------------------------------------------------------- import / export

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!await ConfirmLeaveAsync()) return;
        var path = _dialogs.PickOpenFile("Import environment", "restByAno environment (*.json)|*.json|All files (*.*)|*.*", null);
        if (path is null) return;

        var outcome = await _json.ImportFileAsync(path, default);
        if (!outcome.Succeeded)
        {
            ShowImportFailure(outcome.Errors, outcome.Warnings);
            return;
        }

        var environment = outcome.Definition!;
        var summary = $"Import the environment “{environment.Name}” with {environment.Variables.Count} variable(s)?";
        if (outcome.SecretsNeedingEntry.Count > 0)
            summary += $"{Environment.NewLine}{Environment.NewLine}{outcome.SecretsNeedingEntry.Count} secret variable(s) will be created WITHOUT a value; you must enter them afterwards: {string.Join(", ", outcome.SecretsNeedingEntry)}.";
        if (outcome.Warnings.Count > 0)
            summary += $"{Environment.NewLine}{Environment.NewLine}Ignored content:{Environment.NewLine}{string.Join(Environment.NewLine, outcome.Warnings.Take(8))}";
        var choice = _dialogs.Show(new DialogRequest("Import environment", summary,
            [new DialogButton("Import", IsDefault: true), new DialogButton("Cancel", IsCancel: true)]));
        if (choice != 0) return;

        try
        {
            await _repository.SaveAsync(environment, default);   // one write after the whole file validated
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
            return;
        }

        var item = new EnvironmentListItem(environment.Id, environment.Name);
        Environments.Add(item);
        Selected = item;
        await LoadAsync(item);
        if (outcome.SecretsNeedingEntry.Count > 0)
            ValidationText = $"Enter values for the imported secrets: {string.Join(", ", outcome.SecretsNeedingEntry)}.";
    }

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private async Task ExportAsync()
    {
        if (_loaded is null || _current is null) return;
        var path = _dialogs.PickSaveFile("Export environment", "restByAno environment (*.json)|*.json", $"{SafeFileName(_current.Name)}.environment.json");
        if (path is null) return;
        try
        {
            await _json.ExportFileAsync(_loaded, path, default);
            _dialogs.ShowMessage("Environment exported",
                "Plain variable values were exported as written. Secret variables are exported as requirements only: no secret value is included. Imported secrets must be entered again.", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowMessage("Export failed", $"The file could not be written ({ex.GetType().Name}). Nothing was changed.", path);
        }
    }

    private void ShowImportFailure(IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        var lines = errors.Take(15).ToList();
        if (errors.Count > lines.Count) lines.Add($"… and {errors.Count - lines.Count} more problem(s).");
        _dialogs.Show(new DialogRequest("Import failed",
            "The file was not imported and nothing was changed. Fix these problems and try again:",
            [new DialogButton("OK", IsDefault: true, IsCancel: true)]) { Detail = string.Join(Environment.NewLine, lines) });
    }

    internal static string SafeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "environment" : cleaned;
    }

    private bool HasCurrent() => _current is not null;

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private async Task RenameAsync()
    {
        if (_current is null || _loaded is null) return;
        var name = _dialogs.PromptText("Rename environment", "New name:", _current.Name, "Rename");
        if (name is null || name == _current.Name) return;

        // Only the name is saved here; unsaved variable edits stay pending.
        try
        {
            _loaded = _loaded with { Name = name };
            await _repository.SaveAsync(_loaded, default);
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
            return;
        }

        _current.Name = name;
        SelectedName = name;
    }

    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private async Task DeleteAsync()
    {
        if (_current is null) return;
        var confirmed = _dialogs.Show(new DialogRequest(
            "Delete environment?",
            $"Delete the environment “{_current.Name}” and its variables, including any stored secrets? This cannot be undone.",
            [new DialogButton("Delete environment"), new DialogButton("Cancel", IsDefault: true, IsCancel: true)])) == 0;
        if (!confirmed) return;

        var item = _current;
        try
        {
            await _repository.DeleteAsync(item.Id, default);
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
            return;
        }

        Environments.Remove(item);
        Selected = Environments.FirstOrDefault();
        await LoadAsync(Selected);
    }

    private bool CanSave() => _current is not null && IsDirty;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync() => await SaveCoreAsync();

    private async Task<bool> SaveCoreAsync()
    {
        if (_current is null || _loaded is null) return false;

        var errors = Variables.Validate();
        if (errors.Count > 0)
        {
            ValidationText = string.Join(Environment.NewLine, errors);
            return false;
        }

        // Environment definition and secret changes are written in one transaction.
        var (variables, secretUpdates) = Variables.ToEnvironmentParts();
        var updated = _loaded with { Variables = variables };
        try
        {
            await _repository.SaveAsync(updated, secretUpdates, default);
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
            return false;
        }

        _loaded = updated;
        Variables.ClearSecretEntries();   // the typed secrets are stored now; clear the entry controls
        _baseline = Fingerprint();
        ValidationText = string.Empty;
        RecomputeDirty();
        return true;
    }
}

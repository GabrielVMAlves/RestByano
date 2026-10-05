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

/// <summary>
/// Collections, one level of folders and requests. Structure changes (create, rename, duplicate, move, reorder, delete)
/// are saved immediately; request edits are saved explicitly. Switching away from a request with unsaved edits asks
/// Save / Discard / Cancel first.
/// </summary>
public sealed partial class CollectionTreeViewModel : ObservableObject
{
    private readonly ICollectionRepository _repository;
    private readonly IDialogService _dialogs;
    private readonly RequestEditorViewModel _editor;
    private readonly IWindowLauncher _windows;
    private readonly CollectionJsonService _json;
    private RequestNodeViewModel? _openNode;

    public CollectionTreeViewModel(ICollectionRepository repository, IDialogService dialogs, RequestEditorViewModel editor, IWindowLauncher windows, CollectionJsonService json)
    {
        _repository = repository;
        _dialogs = dialogs;
        _editor = editor;
        _windows = windows;
        _json = json;
        _editor.SaveHandler = SaveOpenRequestAsync;
        _editor.CollectionDefaults = () => _openNode?.OwningCollection?.Defaults ?? [];
        _editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RequestEditorViewModel.IsDirty) && _openNode is not null)
                _openNode.IsDirty = _editor.IsDirty && !_openNode.IsNew;
        };
        Collections.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoCollections));
    }

    public ObservableCollection<CollectionNodeViewModel> Collections { get; } = [];

    public bool HasNoCollections => Collections.Count == 0;

    [ObservableProperty]
    private TreeNodeViewModel? _selectedNode;

    [ObservableProperty]
    private string _statusText = string.Empty;

    // ------------------------------------------------------------------ run support

    /// <summary>
    /// The saved state of the collection that owns the selected item (new unsaved requests are not included). The runner works
    /// on this immutable snapshot, so editing while it runs cannot change the run.
    /// </summary>
    public CollectionDefinition? GetRunSnapshot() => SelectedNode?.OwningCollection?.ToDefinition(applyBack: false);

    // ------------------------------------------------------------------ loading

    public async Task InitializeAsync()
    {
        try
        {
            await ReloadAsync();
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
        }
    }

    private async Task ReloadAsync()
    {
        Collections.Clear();
        _openNode = null;
        SelectedNode = null;

        var problems = new List<string>();
        foreach (var summary in await _repository.ListAsync(default))
        {
            try
            {
                var definition = await _repository.LoadAsync(summary.Id, default);
                if (definition is not null)
                    Collections.Add(BuildCollectionNode(definition));
            }
            catch (StorageException ex)
            {
                problems.Add($"“{summary.Name}”: {ex.Problem.Message}");
            }
        }

        if (problems.Count > 0)
            _dialogs.ShowMessage("Some collections could not be loaded", string.Join(Environment.NewLine, problems));
        NotifyCommands();
    }

    private CollectionNodeViewModel BuildCollectionNode(CollectionDefinition definition)
    {
        var node = new CollectionNodeViewModel(definition.Id, definition.Name, definition.Defaults) { IsExpanded = true };
        Hook(node);

        var folderIds = definition.Folders.Select(f => f.Id).ToHashSet();
        foreach (var folder in definition.Folders.OrderBy(f => f.Order))
        {
            var folderNode = new FolderNodeViewModel(folder.Id, folder.Name);
            Hook(folderNode);
            node.AddChild(folderNode);
            foreach (var request in definition.Requests.Where(r => r.FolderId == folder.Id).OrderBy(r => r.Order))
                folderNode.AddChild(HookRequest(new RequestNodeViewModel(request, isNew: false)));
        }

        foreach (var request in definition.Requests
                     .Where(r => r.FolderId is null || !folderIds.Contains(r.FolderId.Value))
                     .OrderBy(r => r.Order))
            node.AddChild(HookRequest(new RequestNodeViewModel(request with { FolderId = null }, isNew: false)));

        return node;
    }

    private void Hook(TreeNodeViewModel node) => node.Selected += OnNodeSelected;

    private RequestNodeViewModel HookRequest(RequestNodeViewModel node)
    {
        Hook(node);
        return node;
    }

    // ------------------------------------------------------------------ selection and the open request

    private void OnNodeSelected(object? sender, EventArgs e)
    {
        var node = (TreeNodeViewModel)sender!;
        SelectedNode = node;
        NotifyCommands();
        if (node is RequestNodeViewModel request && request != _openNode)
            _ = OpenRequestAsync(request);
    }

    private async Task OpenRequestAsync(RequestNodeViewModel node)
    {
        if (!await ConfirmLeaveAsync())
        {
            // Cancelled: put the selection back on the request that stays open.
            if (node.IsNew && node != _openNode)
                RemoveNode(node);
            var stay = _openNode;
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                node.SetSelectedSilently(false);
                stay?.SetSelectedSilently(true);
                SelectedNode = stay;
                NotifyCommands();
            }));
            return;
        }

        OpenInEditor(node);
    }

    private void OpenInEditor(RequestNodeViewModel node)
    {
        if (_openNode is not null)
            _openNode.IsDirty = false;
        _openNode = node;
        _editor.Load(node.Definition, LocationOf(node), node.IsNew);
        node.IsDirty = false;
        NotifyCommands();
    }

    private static string LocationOf(TreeNodeViewModel node)
    {
        var collection = node.OwningCollection?.Name ?? string.Empty;
        return node.Parent is FolderNodeViewModel folder ? $"{collection} / {folder.Name}" : collection;
    }

    /// <summary>Returns true when it is safe to replace or close the open request (saved, discarded or nothing pending).</summary>
    public async Task<bool> ConfirmLeaveAsync()
    {
        if (_openNode is null || !_editor.IsDirty)
            return true;

        var name = string.IsNullOrWhiteSpace(_editor.Name) ? "This request" : $"“{_editor.Name}”";
        var choice = _dialogs.Show(new DialogRequest(
            "Unsaved changes",
            $"{name} has unsaved changes. Save them before continuing?",
            [
                new DialogButton("Save", IsDefault: true),
                new DialogButton("Discard"),
                new DialogButton("Cancel", IsCancel: true),
            ]));

        switch (choice)
        {
            case 0:
                return await SaveOpenRequestAsync();
            case 1:
                DiscardOpenRequest();
                return true;
            default:
                return false;
        }
    }

    private void DiscardOpenRequest()
    {
        var node = _openNode;
        if (node is null) return;
        if (node.IsNew)
            RemoveNode(node);
        node.IsDirty = false;
        _openNode = null;
        _editor.ResetToScratch();
        NotifyCommands();
    }

    private async Task<bool> SaveOpenRequestAsync()
    {
        var node = _openNode;
        var collection = node?.OwningCollection;
        if (node is null || collection is null)
            return false;

        var edited = _editor.ToDefinition();
        if (string.IsNullOrWhiteSpace(edited.Name))
        {
            _dialogs.ShowMessage("Request name needed", "Enter a name for the request before saving it.");
            return false;
        }

        var ruleErrors = _editor.RuleErrors;
        if (ruleErrors.Count > 0)
        {
            _dialogs.Show(new DialogRequest("Fix the rules before saving", "The assertion/extraction rules have problems, so the request was not saved:",
                [new DialogButton("OK", IsDefault: true, IsCancel: true)]) { Detail = string.Join(Environment.NewLine, ruleErrors) });
            return false;
        }

        var previous = node.Definition;
        var wasNew = node.IsNew;
        node.Definition = edited with { FolderId = previous.FolderId, Order = previous.Order };
        node.IsNew = false;

        if (!await PersistAsync(collection))
        {
            node.Definition = previous;
            node.IsNew = wasNew;
            return false;
        }

        _editor.MarkSaved(node.Definition);
        node.IsDirty = false;
        StatusText = $"Saved “{node.Definition.Name}”.";
        NotifyCommands();
        return true;
    }

    private void RemoveNode(TreeNodeViewModel node)
    {
        node.Parent?.RemoveChild(node);
        if (node == SelectedNode)
            SelectedNode = null;
    }

    // ------------------------------------------------------------------ persistence helpers

    private async Task<bool> PersistAsync(CollectionNodeViewModel collection)
    {
        try
        {
            await _repository.SaveAsync(collection.ToDefinition(applyBack: true), default);
            return true;
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _dialogs.ShowMessage("Could not save", $"The collection could not be saved ({ex.GetType().Name}). Nothing else was changed.");
            return false;
        }
    }

    private async Task<bool> PersistDeleteAsync(Guid collectionId)
    {
        try
        {
            await _repository.DeleteAsync(collectionId, default);
            return true;
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
            return false;
        }
    }

    private bool _busy;

    private bool NotBusy() => !_busy;

    /// <summary>Runs one structural operation at a time; commands are disabled while it runs and re-evaluated after.</summary>
    private async Task RunExclusiveAsync(Func<Task> operation)
    {
        if (_busy) return;
        _busy = true;
        NotifyCommands();
        try
        {
            await operation();
        }
        finally
        {
            _busy = false;
            NotifyCommands();
        }
    }

    private void NotifyCommands()
    {
        NewCollectionCommand.NotifyCanExecuteChanged();
        ImportCollectionCommand.NotifyCanExecuteChanged();
        ExportCollectionCommand.NotifyCanExecuteChanged();
        NewFolderCommand.NotifyCanExecuteChanged();
        EditVariablesCommand.NotifyCanExecuteChanged();
        NewRequestCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        MoveToCommand.NotifyCanExecuteChanged();
    }

    private static TreeNodeViewModel? ContainerFor(TreeNodeViewModel? node) => node switch
    {
        FolderNodeViewModel folder => folder,
        CollectionNodeViewModel collection => collection,
        RequestNodeViewModel request => request.Parent,
        _ => null,
    };

    private void InsertCollectionSorted(CollectionNodeViewModel node)
    {
        var index = 0;
        while (index < Collections.Count && string.Compare(Collections[index].Name, node.Name, StringComparison.CurrentCultureIgnoreCase) <= 0)
            index++;
        Collections.Insert(index, node);
    }

    // ------------------------------------------------------------------ create

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(NotBusy))]
    private Task NewCollectionAsync() => RunExclusiveAsync(NewCollectionCoreAsync);

    private async Task NewCollectionCoreAsync()
    {
        var name = _dialogs.PromptText("New collection", "Name for the new collection:", "New collection", "Create");
        if (name is null) return;

        // New collections start empty: no demo requests.
        var node = new CollectionNodeViewModel(Guid.NewGuid(), name, []);
        Hook(node);
        if (!await PersistAsync(node)) return;

        InsertCollectionSorted(node);
        node.IsSelected = true;
        StatusText = $"Created collection “{name}”.";
    }

    private bool HasSelection() => !_busy && SelectedNode is not null;

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(HasSelection))]
    private Task NewFolderAsync() => RunExclusiveAsync(NewFolderCoreAsync);

    private async Task NewFolderCoreAsync()
    {
        var collection = SelectedNode?.OwningCollection;
        if (collection is null) return;

        var name = _dialogs.PromptText("New folder", $"Name for the new folder in “{collection.Name}”:", "New folder", "Create");
        if (name is null) return;

        var folder = new FolderNodeViewModel(Guid.NewGuid(), name);
        Hook(folder);
        collection.AddChild(folder, collection.Folders.Count());
        if (!await PersistAsync(collection))
        {
            collection.RemoveChild(folder);
            return;
        }

        collection.IsExpanded = true;
        folder.IsSelected = true;
        StatusText = $"Created folder “{name}”.";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void NewRequest()
    {
        var parent = ContainerFor(SelectedNode);
        if (parent is null) return;

        // A new request starts unsaved: it is only stored when the user saves it.
        var definition = new RequestDefinition
        {
            Id = Guid.NewGuid(),
            Name = "New request",
            FolderId = (parent as FolderNodeViewModel)?.Id,
        };
        var node = HookRequest(new RequestNodeViewModel(definition, isNew: true));
        parent.AddChild(node);
        parent.IsExpanded = true;
        parent.OwningCollection!.IsExpanded = true;
        node.IsSelected = true; // opens it (and asks about unsaved changes of the previous request first)
    }

    // ------------------------------------------------------------------ import / export

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(NotBusy))]
    private Task ImportCollectionAsync() => RunExclusiveAsync(ImportCollectionCoreAsync);

    private async Task ImportCollectionCoreAsync()
    {
        var path = _dialogs.PickOpenFile("Import collection", "restByAno collection (*.json)|*.json|All files (*.*)|*.*", null);
        if (path is null) return;

        // The whole file is validated first; nothing is stored unless it is completely valid.
        var outcome = await _json.ImportFileAsync(path, default);
        if (!outcome.Succeeded)
        {
            var lines = outcome.Errors.Take(15).ToList();
            if (outcome.Errors.Count > lines.Count) lines.Add($"… and {outcome.Errors.Count - lines.Count} more problem(s).");
            _dialogs.Show(new DialogRequest("Import failed",
                "The file was not imported and nothing was changed. Fix these problems and try again:",
                [new DialogButton("OK", IsDefault: true, IsCancel: true)]) { Detail = string.Join(Environment.NewLine, lines) });
            return;
        }

        var collection = outcome.Definition!;
        var message = $"Import the collection “{collection.Name}” with {collection.Folders.Count} folder(s) and {collection.Requests.Count} request(s)? It is added as a new collection; existing data is not changed.";
        if (outcome.Warnings.Count > 0)
        {
            var shown = outcome.Warnings.Take(10).ToList();
            if (outcome.Warnings.Count > shown.Count) shown.Add($"… and {outcome.Warnings.Count - shown.Count} more.");
            message += $"{Environment.NewLine}{Environment.NewLine}Unsupported content that will be ignored:";
            if (_dialogs.Show(new DialogRequest("Import collection", message,
                    [new DialogButton("Import", IsDefault: true), new DialogButton("Cancel", IsCancel: true)]) { Detail = string.Join(Environment.NewLine, shown) }) != 0)
                return;
        }
        else if (_dialogs.Show(new DialogRequest("Import collection", message,
                     [new DialogButton("Import", IsDefault: true), new DialogButton("Cancel", IsCancel: true)])) != 0)
        {
            return;
        }

        try
        {
            await _repository.SaveAsync(collection, default);
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
            return;
        }

        var node = BuildCollectionNode(collection);
        InsertCollectionSorted(node);
        node.IsSelected = true;
        StatusText = $"Imported “{collection.Name}” ({collection.Requests.Count} request(s)).";
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(HasSelection))]
    private Task ExportCollectionAsync() => RunExclusiveAsync(ExportCollectionCoreAsync);

    private async Task ExportCollectionCoreAsync()
    {
        var collection = SelectedNode?.OwningCollection;
        if (collection is null) return;

        var path = _dialogs.PickSaveFile("Export collection", "restByAno collection (*.json)|*.json", $"{EnvironmentEditorViewModel.SafeFileName(collection.Name)}.collection.json");
        if (path is null) return;

        try
        {
            // Saved requests only; secrets and run history are never part of an export.
            await _json.ExportFileAsync(collection.ToDefinition(applyBack: false), path, default);
            StatusText = $"Exported “{collection.Name}” to {path}. Request content is exported as written (it may contain business data); secrets and history are not included.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowMessage("Export failed", $"The file could not be written ({ex.GetType().Name}). Nothing was changed.", path);
        }
    }

    // ------------------------------------------------------------------ variables

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(HasSelection))]
    private Task EditVariablesAsync() => RunExclusiveAsync(EditVariablesCoreAsync);

    private async Task EditVariablesCoreAsync()
    {
        var collection = SelectedNode?.OwningCollection;
        if (collection is null) return;

        var edited = _windows.EditCollectionVariables(new CollectionVariablesViewModel(collection.Name, collection.Defaults));
        if (edited is null) return;

        var previous = collection.Defaults;
        collection.Defaults = edited;
        if (!await PersistAsync(collection))
        {
            collection.Defaults = previous;
            return;
        }

        StatusText = $"Saved variables of “{collection.Name}”.";
    }

    // ------------------------------------------------------------------ rename

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(HasSelection))]
    private Task RenameAsync() => RunExclusiveAsync(RenameCoreAsync);

    private async Task RenameCoreAsync()
    {
        switch (SelectedNode)
        {
            case CollectionNodeViewModel collection:
            {
                var name = _dialogs.PromptText("Rename collection", "New name:", collection.Name, "Rename");
                if (name is null || name == collection.Name) return;
                var old = collection.Name;
                collection.Name = name;
                if (!await PersistAsync(collection)) collection.Name = old;
                else StatusText = $"Renamed collection to “{name}”.";
                break;
            }
            case FolderNodeViewModel folder:
            {
                var name = _dialogs.PromptText("Rename folder", "New name:", folder.Name, "Rename");
                if (name is null || name == folder.Name) return;
                var old = folder.Name;
                folder.Name = name;
                if (!await PersistAsync(folder.OwningCollection!)) folder.Name = old;
                else StatusText = $"Renamed folder to “{name}”.";
                break;
            }
            case RequestNodeViewModel request:
            {
                var name = _dialogs.PromptText("Rename request", "New name:", request.Definition.Name, "Rename");
                if (name is null || name == request.Definition.Name) return;
                var previous = request.Definition;
                request.Definition = previous with { Name = name };
                if (request == _openNode) _editor.ApplyRename(name);
                if (request.IsNew) return; // not stored yet; saved with the request

                if (!await PersistAsync(request.OwningCollection!))
                {
                    request.Definition = previous;
                    if (request == _openNode) _editor.ApplyRename(previous.Name);
                }
                else
                {
                    StatusText = $"Renamed request to “{name}”.";
                }

                break;
            }
        }
    }

    // ------------------------------------------------------------------ duplicate

    private bool CanDuplicate() => !_busy && SelectedNode is not null and not RequestNodeViewModel { IsNew: true };

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanDuplicate))]
    private Task DuplicateAsync() => RunExclusiveAsync(DuplicateCoreAsync);

    private async Task DuplicateCoreAsync()
    {
        switch (SelectedNode)
        {
            case CollectionNodeViewModel collection:
            {
                var copyDefinition = DefinitionCloner.CloneCollection(collection.ToDefinition(applyBack: false), $"{collection.Name} copy");
                try
                {
                    await _repository.SaveAsync(copyDefinition, default);
                }
                catch (StorageException ex)
                {
                    _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
                    return;
                }

                var node = BuildCollectionNode(copyDefinition);
                InsertCollectionSorted(node);
                node.IsSelected = true;
                StatusText = $"Duplicated collection as “{node.Name}”.";
                break;
            }
            case FolderNodeViewModel folder:
            {
                var collection = folder.OwningCollection!;
                var requests = folder.Children.OfType<RequestNodeViewModel>().Where(r => !r.IsNew).Select(r => r.Definition);
                var (copyFolder, copyRequests) = DefinitionCloner.CloneFolder(
                    new FolderDefinition { Id = folder.Id, Name = folder.Name }, requests, $"{folder.Name} copy", 0);

                var node = new FolderNodeViewModel(copyFolder.Id, copyFolder.Name);
                Hook(node);
                foreach (var request in copyRequests)
                    node.AddChild(HookRequest(new RequestNodeViewModel(request, isNew: false)));
                collection.AddChild(node, collection.Children.IndexOf(folder) + 1);

                if (!await PersistAsync(collection))
                {
                    collection.RemoveChild(node);
                    return;
                }

                StatusText = $"Duplicated folder as “{node.Name}”.";
                break;
            }
            case RequestNodeViewModel request:
            {
                var parent = request.Parent!;
                // A request that is open with edits is copied as it appears on screen.
                var source = request == _openNode
                    ? _editor.ToDefinition() with { FolderId = request.Definition.FolderId, Order = request.Definition.Order }
                    : request.Definition;
                var copy = DefinitionCloner.CloneRequest(source, $"{source.Name} copy");
                var node = HookRequest(new RequestNodeViewModel(copy, isNew: false));
                parent.AddChild(node, parent.Children.IndexOf(request) + 1);

                if (!await PersistAsync(request.OwningCollection!))
                {
                    parent.RemoveChild(node);
                    return;
                }

                StatusText = $"Duplicated request as “{copy.Name}”.";
                break;
            }
        }

        NotifyCommands();
    }

    // ------------------------------------------------------------------ order and move

    private (TreeNodeViewModel Parent, int Index, int Neighbor)? FindNeighbor(TreeNodeViewModel? node, int step)
    {
        if (node?.Parent is not { } parent || node is RequestNodeViewModel { IsNew: true })
            return null;

        var siblings = parent.Children;
        var index = siblings.IndexOf(node);
        var neighbor = index + step;
        if (index < 0 || neighbor < 0 || neighbor >= siblings.Count)
            return null;

        var other = siblings[neighbor];
        if (other.GetType() != node.GetType() || other is RequestNodeViewModel { IsNew: true })
            return null; // folders and requests are separate groups; unsaved requests stay last

        return (parent, index, neighbor);
    }

    private bool CanMoveUp() => !_busy && FindNeighbor(SelectedNode, -1) is not null;

    private bool CanMoveDown() => !_busy && FindNeighbor(SelectedNode, 1) is not null;

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanMoveUp))]
    private Task MoveUpAsync() => RunExclusiveAsync(() => MoveAsync(-1));

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanMoveDown))]
    private Task MoveDownAsync() => RunExclusiveAsync(() => MoveAsync(1));

    private async Task MoveAsync(int step)
    {
        if (FindNeighbor(SelectedNode, step) is not { } move) return;

        move.Parent.Children.Move(move.Index, move.Neighbor);
        if (!await PersistAsync(move.Parent.OwningCollection ?? (CollectionNodeViewModel)move.Parent))
            move.Parent.Children.Move(move.Neighbor, move.Index);
        NotifyCommands();
    }

    private bool CanMoveTo() => !_busy && SelectedNode is RequestNodeViewModel { IsNew: false };

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanMoveTo))]
    private Task MoveToAsync() => RunExclusiveAsync(MoveToCoreAsync);

    private async Task MoveToCoreAsync()
    {
        if (SelectedNode is not RequestNodeViewModel request) return;
        var collection = request.OwningCollection!;

        var destinations = new List<TreeNodeViewModel>();
        if (request.Parent is not CollectionNodeViewModel) destinations.Add(collection);
        destinations.AddRange(collection.Folders.Where(f => f != request.Parent));
        if (destinations.Count == 0)
        {
            _dialogs.ShowMessage("Nowhere to move to", "Create a folder first, then move requests into it.");
            return;
        }

        var labels = destinations.Select(d => d is FolderNodeViewModel f ? $"Folder: {f.Name}" : "Collection root").ToList();
        if (_dialogs.PickChoice("Move request", $"Move “{request.Definition.Name}” to:", labels, "Move") is not { } picked)
            return;

        var target = destinations[picked];
        var oldParent = request.Parent!;
        var oldIndex = oldParent.Children.IndexOf(request);
        var previous = request.Definition;

        oldParent.RemoveChild(request);
        target.AddChild(request);
        request.Definition = previous with { FolderId = (target as FolderNodeViewModel)?.Id };

        if (!await PersistAsync(collection))
        {
            target.RemoveChild(request);
            oldParent.AddChild(request, oldIndex);
            request.Definition = previous;
            return;
        }

        // Removing a selected item makes WPF select its parent; put the selection back on the moved request.
        target.IsExpanded = true;
        request.SetSelectedSilently(false);
        request.IsSelected = true;
        StatusText = $"Moved “{request.Definition.Name}”.";
    }

    // ------------------------------------------------------------------ delete

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(HasSelection))]
    private Task DeleteAsync() => RunExclusiveAsync(DeleteCoreAsync);

    private async Task DeleteCoreAsync()
    {
        switch (SelectedNode)
        {
            case RequestNodeViewModel request:
                await DeleteRequestAsync(request);
                break;
            case FolderNodeViewModel folder:
                await DeleteFolderAsync(folder);
                break;
            case CollectionNodeViewModel collection:
                await DeleteCollectionAsync(collection);
                break;
        }

        NotifyCommands();
    }

    private bool Confirm(string title, string message, string deleteLabel) =>
        _dialogs.Show(new DialogRequest(title, message,
        [
            new DialogButton(deleteLabel),
            new DialogButton("Cancel", IsDefault: true, IsCancel: true),
        ])) == 0;

    private async Task DeleteRequestAsync(RequestNodeViewModel request)
    {
        var extra = request == _openNode && _editor.IsDirty ? " Its unsaved changes will be lost." : string.Empty;
        if (!Confirm("Delete request?", $"Delete the request “{request.Definition.Name}”? This cannot be undone.{extra}", "Delete request"))
            return;

        var collection = request.OwningCollection!;
        var parent = request.Parent!;
        var index = parent.Children.IndexOf(request);
        var wasOpen = request == _openNode;

        RemoveNode(request);
        if (wasOpen)
        {
            _openNode = null;
            _editor.ResetToScratch();
        }

        if (request.IsNew) return; // never stored
        if (!await PersistAsync(collection))
        {
            parent.AddChild(request, index);
            return;
        }

        StatusText = $"Deleted request “{request.Definition.Name}”.";
    }

    private async Task DeleteFolderAsync(FolderNodeViewModel folder)
    {
        var collection = folder.OwningCollection!;
        var requests = folder.Children.OfType<RequestNodeViewModel>().ToList();
        var openInside = _openNode is not null && requests.Contains(_openNode);

        // Explicit choice required: moving or deleting contained requests is never implied.
        bool moveToRoot;
        if (requests.Count == 0)
        {
            if (!Confirm("Delete folder?", $"Delete the empty folder “{folder.Name}”?", "Delete folder")) return;
            moveToRoot = true;
        }
        else
        {
            var choice = _dialogs.Show(new DialogRequest(
                "Delete folder?",
                $"The folder “{folder.Name}” contains {requests.Count} request(s). What should happen to them?",
                [
                    new DialogButton("Move requests to collection root"),
                    new DialogButton("Delete folder and its requests"),
                    new DialogButton("Cancel", IsDefault: true, IsCancel: true),
                ]));
            if (choice is not (0 or 1)) return;
            moveToRoot = choice == 0;
        }

        var folderIndex = collection.Children.IndexOf(folder);
        var snapshot = requests.Select(r => (Node: r, Definition: r.Definition)).ToList();

        collection.RemoveChild(folder);
        if (moveToRoot)
        {
            foreach (var request in requests)
            {
                folder.RemoveChild(request);
                request.Definition = request.Definition with { FolderId = null };
                collection.AddChild(request);
            }
        }
        else if (openInside)
        {
            _openNode = null;
            _editor.ResetToScratch();
        }

        if (!await PersistAsync(collection))
        {
            // Undo the in-memory change; the stored collection was not changed.
            if (moveToRoot)
                foreach (var (node, definition) in snapshot)
                {
                    collection.RemoveChild(node);
                    node.Definition = definition;
                    folder.AddChild(node);
                }

            collection.AddChild(folder, folderIndex);
            return;
        }

        StatusText = moveToRoot ? $"Deleted folder “{folder.Name}”; its requests moved to the root." : $"Deleted folder “{folder.Name}” and its requests.";
    }

    private async Task DeleteCollectionAsync(CollectionNodeViewModel collection)
    {
        var count = collection.SavedRequests.Count();
        var openInside = _openNode?.OwningCollection == collection;
        var extra = openInside && _editor.IsDirty ? " Unsaved changes in its open request will be lost." : string.Empty;
        if (!Confirm("Delete collection?",
                $"Delete the collection “{collection.Name}” with {collection.Folders.Count()} folder(s) and {count} request(s)? Run history is kept. This cannot be undone.{extra}",
                "Delete collection"))
            return;

        if (!await PersistDeleteAsync(collection.Id)) return;

        if (openInside)
        {
            _openNode = null;
            _editor.ResetToScratch();
        }

        Collections.Remove(collection);
        SelectedNode = null;
        StatusText = $"Deleted collection “{collection.Name}”.";
    }
}

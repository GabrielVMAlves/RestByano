using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using restByAno.Core.Models;

namespace restByAno.Desktop.ViewModels;

/// <summary>Tree item for collections, folders and requests. Desktop-only; the domain records stay immutable.</summary>
public abstract partial class TreeNodeViewModel : ObservableObject
{
    private bool _suppressSelectionEvent;

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    public TreeNodeViewModel? Parent { get; internal set; }

    public abstract string Glyph { get; }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Raised when the user (or a bound TreeView) selects this node.</summary>
    public event EventHandler? Selected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !_suppressSelectionEvent)
            Selected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Changes selection without raising <see cref="Selected"/> (used to revert a cancelled selection).</summary>
    public void SetSelectedSilently(bool value)
    {
        _suppressSelectionEvent = true;
        try
        {
            IsSelected = value;
        }
        finally
        {
            _suppressSelectionEvent = false;
        }
    }

    public void AddChild(TreeNodeViewModel child, int? index = null)
    {
        child.Parent = this;
        if (index is { } i) Children.Insert(i, child);
        else Children.Add(child);
    }

    public void RemoveChild(TreeNodeViewModel child)
    {
        Children.Remove(child);
        child.Parent = null;
    }

    public CollectionNodeViewModel? OwningCollection
    {
        get
        {
            TreeNodeViewModel? node = this;
            while (node is not null and not CollectionNodeViewModel)
                node = node.Parent;
            return node as CollectionNodeViewModel;
        }
    }
}

public sealed partial class CollectionNodeViewModel(Guid id, string name, IReadOnlyList<VariableDefinition> defaults) : TreeNodeViewModel
{
    public Guid Id { get; } = id;

    /// <summary>Non-secret default variables, carried through saves untouched until the environment editor exists.</summary>
    public IReadOnlyList<VariableDefinition> Defaults { get; set; } = defaults;

    public override string Glyph => "▣";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _name = name;

    public string Title => Name;

    public IEnumerable<FolderNodeViewModel> Folders => Children.OfType<FolderNodeViewModel>();

    public IEnumerable<RequestNodeViewModel> RootRequests => Children.OfType<RequestNodeViewModel>();

    public IEnumerable<RequestNodeViewModel> SavedRequests =>
        Folders.SelectMany(f => f.Children.OfType<RequestNodeViewModel>()).Concat(RootRequests).Where(r => !r.IsNew);

    /// <summary>Builds the persisted definition from the tree (saved requests only) and normalizes order values to 0..n-1.</summary>
    public CollectionDefinition ToDefinition(bool applyBack)
    {
        var folders = new List<FolderDefinition>();
        var requests = new List<RequestDefinition>();

        var folderOrder = 0;
        foreach (var folder in Folders)
        {
            folders.Add(new FolderDefinition { Id = folder.Id, Name = folder.Name, Order = folderOrder++ });
            Add(folder.Children.OfType<RequestNodeViewModel>(), folder.Id);
        }

        Add(RootRequests, null);

        void Add(IEnumerable<RequestNodeViewModel> nodes, Guid? folderId)
        {
            var order = 0;
            foreach (var node in nodes.Where(n => !n.IsNew))
            {
                var definition = node.Definition with { FolderId = folderId, Order = order++ };
                requests.Add(definition);
                if (applyBack) node.Definition = definition;
            }
        }

        return new CollectionDefinition
        {
            Id = Id,
            Name = Name,
            Defaults = Defaults,
            Folders = folders,
            Requests = requests,
        };
    }
}

public sealed partial class FolderNodeViewModel(Guid id, string name) : TreeNodeViewModel
{
    public Guid Id { get; } = id;

    public override string Glyph => "▤";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _name = name;

    public string Title => Name;
}

public sealed partial class RequestNodeViewModel : TreeNodeViewModel
{
    public RequestNodeViewModel(RequestDefinition definition, bool isNew)
    {
        _definition = definition;
        IsNew = isNew;
    }

    private RequestDefinition _definition;

    public override string Glyph => "•";

    public RequestDefinition Definition
    {
        get => _definition;
        set
        {
            _definition = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(MethodLabel));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(StateMark))]
    private bool _isNew;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateMark))]
    private bool _isDirty;

    public Guid Id => Definition.Id;

    public string MethodLabel => Definition.Method.ToString().ToUpperInvariant();

    public string Title => IsNew ? $"{Definition.Name} (unsaved)" : Definition.Name;

    /// <summary>Shown before the name when the open request has unsaved edits (glyph, not color alone).</summary>
    public string StateMark => IsNew || IsDirty ? "●" : string.Empty;
}

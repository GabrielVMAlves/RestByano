using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using restByAno.Core.Models;

namespace restByAno.Desktop.ViewModels;

/// <summary>Desktop-only editable row. Mapped to an immutable <see cref="KeyValueEntry"/> before a send.</summary>
public sealed partial class KeyValueRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    public KeyValueEntry ToEntry() => new() { Name = Name, Value = Value, Enabled = Enabled };
}

/// <summary>Ordered rows with duplicate keys allowed; used for query parameters and headers.</summary>
public sealed partial class KeyValueEditorViewModel : ObservableObject
{
    public KeyValueEditorViewModel(string itemLabel)
    {
        ItemLabel = itemLabel;
        Rows.CollectionChanged += OnRowsChanged;
    }

    public string ItemLabel { get; }

    public ObservableCollection<KeyValueRowViewModel> Rows { get; } = [];

    /// <summary>Raised when a row is added, removed or edited.</summary>
    public event EventHandler? Changed;

    [RelayCommand]
    private void Add() => Rows.Add(new KeyValueRowViewModel());

    [RelayCommand]
    private void Remove(KeyValueRowViewModel? row)
    {
        if (row is not null) Rows.Remove(row);
    }

    public IReadOnlyList<KeyValueEntry> ToEntries() => Rows.Select(r => r.ToEntry()).ToList();

    public void Load(IEnumerable<KeyValueEntry> entries)
    {
        while (Rows.Count > 0)
            Rows.RemoveAt(Rows.Count - 1); // item-wise so row handlers are detached
        foreach (var entry in entries)
            Rows.Add(new KeyValueRowViewModel { Enabled = entry.Enabled, Name = entry.Name, Value = entry.Value });
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (KeyValueRowViewModel row in e.OldItems)
                row.PropertyChanged -= OnRowPropertyChanged;
        if (e.NewItems is not null)
            foreach (KeyValueRowViewModel row in e.NewItems)
                row.PropertyChanged += OnRowPropertyChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}

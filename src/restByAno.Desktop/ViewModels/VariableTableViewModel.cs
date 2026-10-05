using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using restByAno.Core.Contracts;
using restByAno.Core.Models;
using restByAno.Core.Resolution;

namespace restByAno.Desktop.ViewModels;

/// <summary>
/// Desktop-only editable variable row. A secret row never holds its stored value: <see cref="NewSecretValue"/> is only
/// the text being typed for the next save, and is cleared as soon as the save succeeds.
/// </summary>
public sealed partial class VariableRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecretHint))]
    private bool _isSecret;

    /// <summary>The id of the already stored secret, if any.</summary>
    public Guid? SecretId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecretHint))]
    private string _newSecretValue = string.Empty;

    /// <summary>Bumps on every keystroke in the secret field so unsaved entry counts as a change without keeping the text anywhere else.</summary>
    public int EntryVersion { get; private set; }

    public string SecretHint => !IsSecret ? string.Empty
        : NewSecretValue.Length > 0 ? "A new value will be stored encrypted when you save."
        : SecretId is not null ? "Stored encrypted. Leave blank to keep it."
        : "Enter the secret value.";

    partial void OnNewSecretValueChanged(string value) => EntryVersion++;

    partial void OnIsSecretChanged(bool value)
    {
        // Never carry a typed plain value into a secret, or a secret into a plain value.
        if (value) Value = string.Empty;
        else NewSecretValue = string.Empty;
    }
}

/// <summary>Editable list of variables with validation (identifier names, no duplicates within the scope).</summary>
public sealed partial class VariableTableViewModel : ObservableObject
{
    public VariableTableViewModel(bool allowSecrets = false)
    {
        AllowSecrets = allowSecrets;
        Rows.CollectionChanged += OnRowsChanged;
    }

    /// <summary>Environments may hold secrets; collection defaults never do.</summary>
    public bool AllowSecrets { get; }

    public ObservableCollection<VariableRowViewModel> Rows { get; } = [];

    public event EventHandler? Changed;

    [RelayCommand]
    private void Add() => Rows.Add(new VariableRowViewModel());

    [RelayCommand]
    private void Remove(VariableRowViewModel? row)
    {
        if (row is not null) Rows.Remove(row);
    }

    /// <summary>Loads rows; a secret whose value is not actually stored (for example after an import) is shown as needing entry.</summary>
    public void Load(IEnumerable<VariableDefinition> variables, IReadOnlySet<Guid>? storedSecretIds = null)
    {
        while (Rows.Count > 0)
            Rows.RemoveAt(Rows.Count - 1);
        foreach (var variable in variables)
        {
            Rows.Add(variable.SecretId is { } secretId
                ? new VariableRowViewModel
                {
                    Name = variable.Name,
                    IsSecret = true,
                    SecretId = storedSecretIds is null || storedSecretIds.Contains(secretId) ? secretId : null,
                }
                : new VariableRowViewModel { Name = variable.Name, Value = variable.Value ?? string.Empty });
        }
    }

    /// <summary>Plain definitions (no secrets). Used for collection defaults.</summary>
    public IReadOnlyList<VariableDefinition> ToDefinitions() =>
        Rows.Select(r => new VariableDefinition { Name = r.Name.Trim(), Value = r.Value }).ToList();

    /// <summary>Definitions for an environment: secret rows reference their secret id (allocated here for new secrets).</summary>
    public (IReadOnlyList<VariableDefinition> Variables, IReadOnlyList<SecretUpdate> Updates) ToEnvironmentParts()
    {
        var variables = new List<VariableDefinition>();
        var updates = new List<SecretUpdate>();
        foreach (var row in Rows)
        {
            var name = row.Name.Trim();
            if (!row.IsSecret)
            {
                variables.Add(new VariableDefinition { Name = name, Value = row.Value });
                continue;
            }

            row.SecretId ??= Guid.NewGuid();
            variables.Add(new VariableDefinition { Name = name, SecretId = row.SecretId });
            updates.Add(new SecretUpdate(row.SecretId.Value, name, row.NewSecretValue.Length > 0 ? row.NewSecretValue : null));
        }

        return (variables, updates);
    }

    /// <summary>Clears every secret entry after a successful save (the stored secret now exists).</summary>
    public void ClearSecretEntries()
    {
        foreach (var row in Rows.Where(r => r.IsSecret))
            row.NewSecretValue = string.Empty;
    }

    /// <summary>Validation messages; they name variables and never show values.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var row in Rows)
        {
            number++;
            var name = row.Name.Trim();
            if (name.Length == 0)
                errors.Add($"Row {number} has no name.");
            else if (!VariableNames.IsValid(name))
                errors.Add($"“{name}” is not a valid name: use letters, digits and underscores, not starting with a digit.");
            else if (!seen.Add(name))
                errors.Add($"“{name}” is defined more than once; names are case-sensitive and must be unique.");

            if (row.IsSecret && row.SecretId is null && row.NewSecretValue.Length == 0)
                errors.Add($"The secret “{(name.Length > 0 ? name : $"row {number}")}” needs a value.");
        }

        return errors;
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (VariableRowViewModel row in e.OldItems)
                row.PropertyChanged -= OnRowPropertyChanged;
        if (e.NewItems is not null)
            foreach (VariableRowViewModel row in e.NewItems)
                row.PropertyChanged += OnRowPropertyChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}

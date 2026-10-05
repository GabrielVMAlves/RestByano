using CommunityToolkit.Mvvm.ComponentModel;
using restByAno.Core.Models;

namespace restByAno.Desktop.ViewModels;

/// <summary>Edits the non-secret default variables of one collection (lowest precedence, overridden by the environment).</summary>
public sealed partial class CollectionVariablesViewModel : ObservableObject
{
    public CollectionVariablesViewModel(string collectionName, IReadOnlyList<VariableDefinition> defaults)
    {
        CollectionName = collectionName;
        Table.Load(defaults);
    }

    public string CollectionName { get; }

    public VariableTableViewModel Table { get; } = new();

    [ObservableProperty]
    private string _validationText = string.Empty;

    /// <summary>Validates; returns the edited defaults or null with <see cref="ValidationText"/> set.</summary>
    public IReadOnlyList<VariableDefinition>? TryAccept()
    {
        var errors = Table.Validate();
        ValidationText = string.Join(Environment.NewLine, errors);
        return errors.Count == 0 ? Table.ToDefinitions() : null;
    }
}

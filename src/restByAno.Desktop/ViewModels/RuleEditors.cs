using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using restByAno.Core.Assertions;
using restByAno.Core.Models;

namespace restByAno.Desktop.ViewModels;

/// <summary>Editable assertion row with field-level validation. Mapped to an immutable <see cref="AssertionDefinition"/>.</summary>
public sealed partial class AssertionRowViewModel : ObservableObject
{
    public AssertionRowViewModel(Guid? id = null) => Id = id ?? Guid.NewGuid();

    public Guid Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHeader), nameof(ShowPointer), nameof(ShowExpected), nameof(ShowStatus), nameof(ShowDuration), nameof(ExpectedLabel), nameof(Hint), nameof(Error), nameof(HasError))]
    private AssertionKind _kind = AssertionKind.StatusEquals;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(HasError))]
    private string _headerName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(HasError))]
    private string _jsonPointer = string.Empty;

    /// <summary>Header value as plain text, or a JSON scalar (e.g. "Ada", 42, true, null) for JSON equality.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(HasError))]
    private string _expectedText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(HasError))]
    private string _statusText = "200";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(HasError))]
    private string _durationText = "1000";

    public bool ShowHeader => Kind == AssertionKind.HeaderEquals;
    public bool ShowPointer => Kind is AssertionKind.JsonExists or AssertionKind.JsonEquals;
    public bool ShowExpected => Kind is AssertionKind.HeaderEquals or AssertionKind.JsonEquals;
    public bool ShowStatus => Kind == AssertionKind.StatusEquals;
    public bool ShowDuration => Kind == AssertionKind.DurationBelow;

    public string ExpectedLabel => Kind == AssertionKind.HeaderEquals ? "Expected value" : "Expected JSON value";

    public string Hint => Kind switch
    {
        AssertionKind.StatusEquals => "The numeric HTTP status must equal this code.",
        AssertionKind.HeaderEquals => "Header name is case-insensitive; the value must match exactly (case-sensitive) one of the returned values.",
        AssertionKind.JsonExists => "The JSON pointer must resolve, even to null. Example: /data/id (the empty pointer is the whole body).",
        AssertionKind.JsonEquals => "Scalar comparison by type: \"1\" is not 1. Strings need quotes, e.g. \"Ada\"; also 42, true, false or null.",
        AssertionKind.DurationBelow => "Passes only when the response time is strictly below the limit (equal to the limit fails).",
        _ => string.Empty,
    };

    public string? Error => RuleValidator.ValidateAssertion(Build(forValidation: true));

    public bool HasError => Error is not null;

    public AssertionDefinition ToDefinition() => Build(forValidation: false);

    private AssertionDefinition Build(bool forValidation) => new()
    {
        Id = Id,
        Kind = Kind,
        HeaderName = Kind == AssertionKind.HeaderEquals ? HeaderName.Trim() : null,
        JsonPointer = Kind is AssertionKind.JsonExists or AssertionKind.JsonEquals ? JsonPointer : null,
        ExpectedJson = Kind switch
        {
            AssertionKind.HeaderEquals => JsonSerializer.Serialize(ExpectedText),
            AssertionKind.JsonEquals => ExpectedText.Trim(),
            _ => null,
        },
        ExpectedStatus = Kind == AssertionKind.StatusEquals && int.TryParse(StatusText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : null,
        MaxMilliseconds = Kind == AssertionKind.DurationBelow && int.TryParse(DurationText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var d) ? d : null,
    };

    public static AssertionRowViewModel From(AssertionDefinition a)
    {
        var row = new AssertionRowViewModel(a.Id) { Kind = a.Kind };
        switch (a.Kind)
        {
            case AssertionKind.StatusEquals:
                row.StatusText = a.ExpectedStatus?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                break;
            case AssertionKind.HeaderEquals:
                row.HeaderName = a.HeaderName ?? string.Empty;
                try
                {
                    row.ExpectedText = a.ExpectedJson is null ? string.Empty : JsonSerializer.Deserialize<string>(a.ExpectedJson) ?? string.Empty;
                }
                catch (JsonException)
                {
                    row.ExpectedText = string.Empty;
                }

                break;
            case AssertionKind.JsonExists:
                row.JsonPointer = a.JsonPointer ?? string.Empty;
                break;
            case AssertionKind.JsonEquals:
                row.JsonPointer = a.JsonPointer ?? string.Empty;
                row.ExpectedText = a.ExpectedJson ?? string.Empty;
                break;
            case AssertionKind.DurationBelow:
                row.DurationText = a.MaxMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                break;
        }

        return row;
    }
}

public sealed partial class ExtractorRowViewModel : ObservableObject
{
    public ExtractorRowViewModel(Guid? id = null) => Id = id ?? Guid.NewGuid();

    public Guid Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(HasError))]
    private string _jsonPointer = "/id";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(HasError))]
    private string _variableName = string.Empty;

    public string? Error => RuleValidator.ValidateExtractor(ToDefinition());

    public bool HasError => Error is not null;

    public ExtractorDefinition ToDefinition() => new() { Id = Id, JsonPointer = JsonPointer, VariableName = VariableName.Trim(), Sensitive = true };
}

public sealed class ChoiceOptions
{
    public static IReadOnlyList<ChoiceOption<AssertionKind>> AssertionKinds { get; } =
    [
        new(AssertionKind.StatusEquals, "Status code equals"),
        new(AssertionKind.HeaderEquals, "Header equals"),
        new(AssertionKind.JsonExists, "JSON path exists"),
        new(AssertionKind.JsonEquals, "JSON value equals"),
        new(AssertionKind.DurationBelow, "Response time below"),
    ];
}

/// <summary>Ordered list of assertions with add/remove/reorder and validation.</summary>
public sealed partial class AssertionsEditorViewModel : ObservableObject
{
    public AssertionsEditorViewModel() => Rows.CollectionChanged += OnRowsChanged;

    public ObservableCollection<AssertionRowViewModel> Rows { get; } = [];

    public IReadOnlyList<ChoiceOption<AssertionKind>> KindOptions => ChoiceOptions.AssertionKinds;

    public event EventHandler? Changed;

    [RelayCommand]
    private void Add() => Rows.Add(new AssertionRowViewModel());

    [RelayCommand]
    private void Remove(AssertionRowViewModel? row)
    {
        if (row is not null) Rows.Remove(row);
    }

    [RelayCommand]
    private void MoveUp(AssertionRowViewModel? row) => Move(row, -1);

    [RelayCommand]
    private void MoveDown(AssertionRowViewModel? row) => Move(row, 1);

    private void Move(AssertionRowViewModel? row, int step)
    {
        if (row is null) return;
        var index = Rows.IndexOf(row);
        var target = index + step;
        if (index >= 0 && target >= 0 && target < Rows.Count)
            Rows.Move(index, target);
    }

    public void Load(IEnumerable<AssertionDefinition> assertions)
    {
        while (Rows.Count > 0)
            Rows.RemoveAt(Rows.Count - 1);
        foreach (var assertion in assertions)
            Rows.Add(AssertionRowViewModel.From(assertion));
    }

    /// <summary>Valid rows only; invalid rows block saving and sending until fixed.</summary>
    public IReadOnlyList<AssertionDefinition> ToDefinitions() => Rows.Where(r => !r.HasError).Select(r => r.ToDefinition()).ToList();

    public IReadOnlyList<string> Validate() =>
        Rows.Select((r, i) => (Row: r, Index: i + 1)).Where(x => x.Row.HasError).Select(x => $"Assertion {x.Index}: {x.Row.Error}").ToList();

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (AssertionRowViewModel row in e.OldItems)
                row.PropertyChanged -= OnRowPropertyChanged;
        if (e.NewItems is not null)
            foreach (AssertionRowViewModel row in e.NewItems)
                row.PropertyChanged += OnRowPropertyChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Ordered list of extractors with add/remove/reorder and validation (valid names, no duplicates).</summary>
public sealed partial class ExtractorsEditorViewModel : ObservableObject
{
    public ExtractorsEditorViewModel() => Rows.CollectionChanged += OnRowsChanged;

    public ObservableCollection<ExtractorRowViewModel> Rows { get; } = [];

    public event EventHandler? Changed;

    [RelayCommand]
    private void Add() => Rows.Add(new ExtractorRowViewModel());

    [RelayCommand]
    private void Remove(ExtractorRowViewModel? row)
    {
        if (row is not null) Rows.Remove(row);
    }

    [RelayCommand]
    private void MoveUp(ExtractorRowViewModel? row) => Move(row, -1);

    [RelayCommand]
    private void MoveDown(ExtractorRowViewModel? row) => Move(row, 1);

    private void Move(ExtractorRowViewModel? row, int step)
    {
        if (row is null) return;
        var index = Rows.IndexOf(row);
        var target = index + step;
        if (index >= 0 && target >= 0 && target < Rows.Count)
            Rows.Move(index, target);
    }

    public void Load(IEnumerable<ExtractorDefinition> extractors)
    {
        while (Rows.Count > 0)
            Rows.RemoveAt(Rows.Count - 1);
        foreach (var extractor in extractors)
            Rows.Add(new ExtractorRowViewModel(extractor.Id) { JsonPointer = extractor.JsonPointer, VariableName = extractor.VariableName });
    }

    public IReadOnlyList<ExtractorDefinition> ToDefinitions() => Rows.Where(r => !r.HasError).Select(r => r.ToDefinition()).ToList();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var row in Rows)
        {
            number++;
            if (row.HasError) errors.Add($"Extractor {number}: {row.Error}");
            else if (!seen.Add(row.VariableName.Trim())) errors.Add($"Extractor {number}: the variable “{row.VariableName.Trim()}” is extracted more than once.");
        }

        return errors;
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ExtractorRowViewModel row in e.OldItems)
                row.PropertyChanged -= OnRowPropertyChanged;
        if (e.NewItems is not null)
            foreach (ExtractorRowViewModel row in e.NewItems)
                row.PropertyChanged += OnRowPropertyChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}

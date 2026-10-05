using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using restByAno.Core.Contracts;
using restByAno.Core.Models;
using restByAno.Desktop.Services;
using restByAno.Infrastructure.ImportExport;
using restByAno.Infrastructure.Persistence;

namespace restByAno.Desktop.ViewModels;

public sealed class HistoryItemViewModel(RunRecord record, string collectionName)
{
    public RunRecord Record { get; } = record;
    public string CollectionName { get; } = collectionName;
    public string Started => Record.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string OutcomeText => Record.Outcome switch
    {
        RunOutcome.Passed => "✔ Passed",
        RunOutcome.Cancelled => "⊘ Cancelled",
        _ => "✖ Failed",
    };

    public StatusTone Tone => Record.Outcome switch
    {
        RunOutcome.Passed => StatusTone.Success,
        RunOutcome.Cancelled => StatusTone.Info,
        _ => StatusTone.Error,
    };

    public string Summary
    {
        get
        {
            var steps = Record.Steps;
            var failed = steps.Count(s => s.Outcome is StepOutcome.AssertionFailed or StepOutcome.ExtractionFailed or StepOutcome.TransportFailed or StepOutcome.ValidationFailed);
            return $"{steps.Count} step(s), {failed} failed · {(Record.EndedUtc - Record.StartedUtc).TotalSeconds:0.0} s";
        }
    }
}

/// <summary>
/// Run history (metadata only) with retention purge at startup, JSON report export and an explicit "Clear history" that only
/// touches the runs table — collections, environments and secrets are never affected.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private const int ListLimit = 200;

    private readonly IRunRepository _runs;
    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;
    private readonly JsonReportExporter _exporter;
    private readonly CollectionTreeViewModel _tree;

    public HistoryViewModel(
        IRunRepository runs, IDialogService dialogs, SettingsService settings, JsonReportExporter exporter,
        CollectionTreeViewModel tree, RunnerViewModel runner)
    {
        _runs = runs;
        _dialogs = dialogs;
        _settings = settings;
        _exporter = exporter;
        _tree = tree;
        runner.RunFinished += async (_, _) => await RefreshAsync();
    }

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    public ObservableCollection<RunStepViewModel> Steps { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    private HistoryItemViewModel? _selected;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public string RetentionText => $"History is kept for {_settings.Current.RetentionDays} day(s) (File → Settings).";

    /// <summary>Applies the retention setting once at startup, then loads the list.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-_settings.Current.RetentionDays);
            var removed = await _runs.PurgeAsync(cutoff, default);
            if (removed > 0)
                StatusText = $"Removed {removed} run(s) older than {_settings.Current.RetentionDays} day(s).";
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var selectedId = Selected?.Record.Id;
            var records = await _runs.ListAsync(null, ListLimit, default);
            Items.Clear();
            foreach (var record in records)
            {
                var name = _tree.Collections.FirstOrDefault(c => c.Id == record.CollectionId)?.Name ?? "(deleted collection)";
                Items.Add(new HistoryItemViewModel(record, name));
            }

            Selected = Items.FirstOrDefault(i => i.Record.Id == selectedId) ?? Items.FirstOrDefault();
            OnPropertyChanged(nameof(RetentionText));
        }
        catch (StorageException ex)
        {
            StatusText = ex.Problem.Message;
        }
    }

    partial void OnSelectedChanged(HistoryItemViewModel? value)
    {
        Steps.Clear();
        if (value is null) return;
        foreach (var step in value.Record.Steps)
        {
            var row = new RunStepViewModel(step.Sequence, step.RequestName, step.IsCleanup, step.HadNoAssertions);
            row.Apply(step);
            Steps.Add(row);
        }
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ExportReportAsync()
    {
        if (Selected is not { } item) return;
        var name = $"run-{item.Record.StartedUtc.ToLocalTime():yyyyMMdd-HHmmss}.report.json";
        var path = _dialogs.PickSaveFile("Save run report", "restByAno run report (*.json)|*.json", name);
        if (path is null) return;

        try
        {
            await _exporter.ExportFileAsync(item.Record, path, default);
            StatusText = $"Report saved to {path}. It contains run metadata and step results only (no URLs, headers, bodies or extracted values).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _dialogs.ShowMessage("Report could not be saved", $"{ex.Message} Nothing was written.", path);
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var confirmed = _dialogs.Show(new DialogRequest(
            "Clear run history?",
            "All saved run history will be deleted. Your collections, environments and secrets are not affected. This cannot be undone.",
            [new DialogButton("Clear history"), new DialogButton("Cancel", IsDefault: true, IsCancel: true)])) == 0;
        if (!confirmed) return;

        try
        {
            var removed = await _runs.PurgeAsync(DateTimeOffset.MaxValue, default);
            StatusText = $"Cleared {removed} run(s) from the history.";
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
            return;
        }

        await RefreshAsync();
    }
}

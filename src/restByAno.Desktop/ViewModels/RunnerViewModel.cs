using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using restByAno.Core.Contracts;
using restByAno.Core.Models;
using restByAno.Core.Running;
using restByAno.Desktop.Services;
using restByAno.Infrastructure.Persistence;

namespace restByAno.Desktop.ViewModels;

/// <summary>Shared flag: while a collection run is active the standalone Send is disabled (one run at a time).</summary>
public sealed partial class RunState : ObservableObject
{
    [ObservableProperty]
    private bool _isRunning;
}

/// <summary>One row of the runner list: glyph + outcome text (never colour alone), timing and a safe message.</summary>
public sealed partial class RunStepViewModel : ObservableObject
{
    public RunStepViewModel(int sequence, string name, bool isCleanup, bool hasNoAssertions)
    {
        Sequence = sequence;
        Name = name;
        IsCleanup = isCleanup;
        HasNoAssertions = hasNoAssertions;
    }

    public int Sequence { get; }
    public string Name { get; }
    public bool IsCleanup { get; }
    public bool HasNoAssertions { get; }

    public string KindLabel => IsCleanup ? "cleanup" : string.Empty;

    [ObservableProperty]
    private string _glyph = "○";

    [ObservableProperty]
    private string _outcomeText = "Pending";

    [ObservableProperty]
    private StatusTone _tone = StatusTone.None;

    [ObservableProperty]
    private string _detail = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    public void Apply(StepRecord step)
    {
        (Glyph, OutcomeText, Tone) = step.Outcome switch
        {
            StepOutcome.Passed => ("✔", "Passed", StatusTone.Success),
            StepOutcome.AssertionFailed => ("✖", "Assertion failed", StatusTone.Error),
            StepOutcome.ExtractionFailed => ("✖", "Extraction failed", StatusTone.Error),
            StepOutcome.TransportFailed => ("✖", "Request failed", StatusTone.Error),
            StepOutcome.ValidationFailed => ("✖", "Not sent (validation)", StatusTone.Error),
            StepOutcome.Cancelled => ("⊘", "Cancelled", StatusTone.Info),
            _ => ("–", "Skipped", StatusTone.None),
        };

        var parts = new List<string>();
        if (step.StatusCode is { } code) parts.Add($"HTTP {code}");
        if (step.ElapsedMilliseconds is { } ms) parts.Add($"{ms} ms");
        if (step.BodyBytes is { } bytes) parts.Add($"{bytes:N0} B");
        if (step.Outcome == StepOutcome.Passed && step.HadNoAssertions) parts.Add("no assertions");
        else if (step.Assertions.Count > 0) parts.Add($"{step.Assertions.Count(a => a.Passed)}/{step.Assertions.Count} assertions");
        Detail = string.Join(" · ", parts);
        Message = step.Message ?? string.Empty;
    }
}

/// <summary>Runs the selected collection with the active environment and shows live progress. One run at a time.</summary>
public sealed partial class RunnerViewModel : ObservableObject
{
    private readonly CollectionTreeViewModel _tree;
    private readonly ActiveEnvironmentViewModel _environment;
    private readonly SettingsService _settings;
    private readonly CollectionRunner _runner;
    private readonly RunState _runState;
    private readonly IDialogService _dialogs;
    private readonly IRunRepository _runs;
    private CancellationTokenSource? _cancellation;

    public RunnerViewModel(
        CollectionTreeViewModel tree, ActiveEnvironmentViewModel environment, SettingsService settings,
        CollectionRunner runner, RunState runState, IDialogService dialogs, IRunRepository runs)
    {
        _tree = tree;
        _environment = environment;
        _settings = settings;
        _runner = runner;
        _runState = runState;
        _dialogs = dialogs;
        _runs = runs;
    }

    public ObservableCollection<RunStepViewModel> Steps { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _stopOnFailure = true;

    [ObservableProperty]
    private string _statusGlyph = "○";

    [ObservableProperty]
    private string _statusText = "No run yet";

    [ObservableProperty]
    private StatusTone _tone = StatusTone.None;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>The last finished run (also saved to the history).</summary>
    public RunRecord? LastRun { get; private set; }

    public event EventHandler<RunRecord>? RunFinished;

    private bool CanRun() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (IsRunning) return;

        var collection = _tree.GetRunSnapshot();
        if (collection is null)
        {
            SetStatus("○", "Select a collection (or a request in it) to run", StatusTone.None);
            return;
        }

        Steps.Clear();
        SummaryText = string.Empty;
        SetStatus("…", $"Preparing “{collection.Name}”", StatusTone.None);

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        IsRunning = true;
        _runState.IsRunning = true;
        try
        {
            var environment = await _environment.LoadActiveAsync(cancellation.Token);
            var transport = TransportProfile.ForSend(_settings.Current.TimeoutSeconds, _settings.Current.ResponseLimitBytes, environment?.TransportProfile);

            // Show the plan up front: ordinary requests in saved order, then cleanup.
            var ordered = CollectionLayout.ExecutionOrder(collection);
            var plan = ordered.Where(r => !r.IsCleanup).Concat(ordered.Where(r => r.IsCleanup)).ToList();
            var number = 0;
            foreach (var request in plan)
                Steps.Add(new RunStepViewModel(++number, request.Name, request.IsCleanup, request.Assertions.Count == 0));

            SetStatus("…", $"Running “{collection.Name}” ({plan.Count} step(s))" + (environment is null ? " with no environment" : $" in “{environment.Name}”"), StatusTone.None);
            var progress = new Progress<RunProgress>(OnProgress);
            var record = await _runner.RunAsync(new RunConfiguration(collection, environment, transport, StopOnFailure), progress, cancellation.Token);
            await Task.Yield(); // let queued progress notifications land before the summary replaces them

            LastRun = record;
            ShowFinished(record);
            await SaveHistoryAsync(record);
            RunFinished?.Invoke(this, record);
        }
        catch (CollectionRunValidationException ex)
        {
            Steps.Clear();
            SetStatus("✖", "The collection cannot be run", StatusTone.Error);
            SummaryText = string.Join(Environment.NewLine, ex.Errors);
        }
        catch (StorageException ex)
        {
            Steps.Clear();
            SetStatus("✖", "The run could not start", StatusTone.Error);
            SummaryText = ex.Problem.Message;
        }
        catch (OperationCanceledException)
        {
            SetStatus("⊘", "Run cancelled", StatusTone.Info);
        }
        finally
        {
            _cancellation = null;
            IsRunning = false;
            _runState.IsRunning = false;
        }
    }

    /// <summary>Stores the metadata-only record. A storage problem is reported but never hides the result of the run.</summary>
    private async Task SaveHistoryAsync(RunRecord record)
    {
        try
        {
            await _runs.SaveAsync(record, default);
        }
        catch (StorageException ex)
        {
            SummaryText += $"  History could not be saved: {ex.Problem.Message}";
        }
    }

    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        SetStatus("⊘", "Cancelling… the request in flight is stopped and no further requests (including cleanup) are sent", StatusTone.Info);
        _cancellation?.Cancel();
    }

    /// <summary>Stops a run in progress (used when the window is closing).</summary>
    public void CancelRun() => _cancellation?.Cancel();

    private void OnProgress(RunProgress progress)
    {
        if (progress.Step is { } step && step.Sequence - 1 is var index && index >= 0 && index < Steps.Count)
            Steps[index].Apply(step);
        StatusText = $"Running… {progress.CompletedSteps} of {progress.TotalSteps} step(s) done";
    }

    private void ShowFinished(RunRecord run)
    {
        var ordinary = run.Steps.Where(s => !s.IsCleanup).ToList();
        var cleanup = run.Steps.Where(s => s.IsCleanup).ToList();
        int Count(IEnumerable<StepRecord> steps, StepOutcome o) => steps.Count(s => s.Outcome == o);
        var failed = ordinary.Count(s => s.Outcome is StepOutcome.AssertionFailed or StepOutcome.ExtractionFailed or StepOutcome.TransportFailed or StepOutcome.ValidationFailed);
        var cleanupFailed = cleanup.Count(s => s.Outcome is StepOutcome.AssertionFailed or StepOutcome.ExtractionFailed or StepOutcome.TransportFailed or StepOutcome.ValidationFailed);

        var (glyph, text, tone) = run.Outcome switch
        {
            RunOutcome.Passed => ("✔", "Run passed", StatusTone.Success),
            RunOutcome.Cancelled => ("⊘", "Run cancelled", StatusTone.Info),
            _ => ("✖", "Run failed", StatusTone.Error),
        };
        SetStatus(glyph, text, tone);

        var parts = new List<string>
        {
            $"{Count(ordinary, StepOutcome.Passed)} passed, {failed} failed, {Count(ordinary, StepOutcome.Skipped) + Count(ordinary, StepOutcome.Cancelled)} skipped/cancelled",
        };
        if (cleanup.Count > 0)
            parts.Add($"cleanup: {Count(cleanup, StepOutcome.Passed)} ok, {cleanupFailed} failed, {Count(cleanup, StepOutcome.Skipped)} skipped");
        parts.Add($"{(run.EndedUtc - run.StartedUtc).TotalSeconds:0.0} s");
        SummaryText = string.Join(" · ", parts);
    }

    private void SetStatus(string glyph, string text, StatusTone tone)
    {
        StatusGlyph = glyph;
        StatusText = text;
        Tone = tone;
    }
}

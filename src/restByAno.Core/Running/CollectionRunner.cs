using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using restByAno.Core.Contracts;
using restByAno.Core.Models;
using restByAno.Core.Resolution;

namespace restByAno.Core.Running;

/// <summary>What the caller wants run. Everything here is an immutable snapshot taken when the run starts.</summary>
public sealed record RunConfiguration(
    CollectionDefinition Collection,
    EnvironmentDefinition? Environment,
    TransportProfile Transport,
    bool StopOnFailure);

/// <summary>Progress after each step (and on cancellation). <see cref="Text"/> never contains secrets.</summary>
public sealed record RunProgress(int CompletedSteps, int TotalSteps, StepRecord? Step, string Text);

public sealed class CollectionRunValidationException(IReadOnlyList<string> errors) : Exception("The collection cannot be run.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Runs a collection one request at a time: ordinary requests in saved order, then every cleanup request once, each through the
/// same resolve → send → assert → extract pipeline. Run variables extracted by a step are available to later steps.
/// </summary>
public sealed class CollectionRunner(IRequestExecutor executor, VariableSnapshotFactory snapshots)
{
    private static readonly JsonSerializerOptions HashOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Throws <see cref="CollectionRunValidationException"/> when the saved collection cannot be run at all.</summary>
    public async Task<RunRecord> RunAsync(RunConfiguration configuration, IProgress<RunProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var errors = CollectionValidator.Validate(configuration.Collection);
        if (errors.Count > 0)
            throw new CollectionRunValidationException(errors);

        var ordered = CollectionLayout.ExecutionOrder(configuration.Collection);
        var ordinary = ordered.Where(r => !r.IsCleanup).ToList();
        var cleanup = ordered.Where(r => r.IsCleanup).ToList();
        var total = ordinary.Count + cleanup.Count;

        var started = DateTimeOffset.UtcNow;
        var runVariables = new Dictionary<string, string>(StringComparer.Ordinal);   // sensitive; ends with the run
        var redaction = new RedactionContext();
        var steps = new List<StepRecord>(total);
        var sequence = 0;
        var cancelled = false;
        var failedStop = false;

        void Report(StepRecord step, string text)
        {
            steps.Add(step);
            progress?.Report(new RunProgress(steps.Count, total, step, redaction.Redact(text)));
        }

        foreach (var request in ordinary)
        {
            sequence++;
            if (cancelled || cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                Report(Skipped(request, sequence, isCleanup: false, "Skipped because the run was cancelled."), $"Skipped “{request.Name}” (cancelled).");
                continue;
            }

            if (failedStop)
            {
                Report(Skipped(request, sequence, isCleanup: false, "Skipped because an earlier step failed (stop on first failure)."), $"Skipped “{request.Name}”.");
                continue;
            }

            var step = await ExecuteStepAsync(request, sequence, isCleanup: false, configuration, runVariables, redaction, cancellationToken);
            if (step.Outcome == StepOutcome.Cancelled) cancelled = true;
            else if (IsFailure(step.Outcome) && configuration.StopOnFailure) failedStop = true;
            Report(step, DescribeProgress(step));
        }

        // Cleanup runs once, even after failures, but never after a cancellation (no new sends once cancelled).
        foreach (var request in cleanup)
        {
            sequence++;
            if (cancelled || cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                Report(Skipped(request, sequence, isCleanup: true, "Cleanup skipped because the run was cancelled."), $"Cleanup “{request.Name}” skipped (cancelled).");
                continue;
            }

            var step = await ExecuteStepAsync(request, sequence, isCleanup: true, configuration, runVariables, redaction, cancellationToken);
            if (step.Outcome == StepOutcome.Cancelled) cancelled = true;
            Report(step, DescribeProgress(step));   // a failed cleanup does not stop the remaining cleanup requests
        }

        var outcome = cancelled ? RunOutcome.Cancelled
            : steps.Any(s => IsFailure(s.Outcome)) ? RunOutcome.Failed
            : RunOutcome.Passed;

        return new RunRecord
        {
            Id = Guid.NewGuid(),
            CollectionId = configuration.Collection.Id,
            EnvironmentId = configuration.Environment?.Id,
            StartedUtc = started,
            EndedUtc = DateTimeOffset.UtcNow,
            Outcome = outcome,
            SnapshotHash = ComputeSnapshotHash(configuration),
            Steps = steps,
        };
    }

    private static bool IsFailure(StepOutcome outcome) =>
        outcome is StepOutcome.AssertionFailed or StepOutcome.ExtractionFailed or StepOutcome.TransportFailed or StepOutcome.ValidationFailed;

    private async Task<StepRecord> ExecuteStepAsync(
        RequestDefinition request, int sequence, bool isCleanup, RunConfiguration configuration,
        Dictionary<string, string> runVariables, RedactionContext redaction, CancellationToken cancellationToken)
    {
        StepRecord Make(StepOutcome outcome, string? message = null, ResponseSnapshot? response = null, ExecutionError? error = null,
            StepCheckResult? check = null) => new()
        {
            StepId = Guid.NewGuid(),
            RequestId = request.Id,
            RequestName = request.Name,
            Sequence = sequence,
            IsCleanup = isCleanup,
            Outcome = outcome,
            StatusCode = (response ?? error?.PartialResponse)?.StatusCode,
            ElapsedMilliseconds = response?.ElapsedMilliseconds ?? error?.ElapsedMilliseconds,
            BodyBytes = response?.BodyLength,
            ErrorKind = error?.Kind,
            Message = message is null ? null : redaction.Redact(message),
            HadNoAssertions = request.Assertions.Count == 0,
            Assertions = check?.Assertions.Select(a => a.Result).ToList() ?? [],
        };

        try
        {
            // Variables are frozen for this step: run values extracted so far, the environment snapshot, collection defaults.
            var variables = await snapshots.BuildAsync(
                configuration.Collection.Defaults, configuration.Environment, runVariables, cancellationToken).ConfigureAwait(false);
            redaction.Add(variables.SecretValues);

            var build = RequestBuilder.Build(request, configuration.Transport, variables);
            if (build.Request is null)
                return Make(StepOutcome.ValidationFailed, string.Join(" ", build.Errors));   // names only; no request is sent

            var result = await executor.ExecuteAsync(build.Request, cancellationToken).ConfigureAwait(false);
            if (result.Response is not { } response)
            {
                var error = result.Error!;
                return error.Kind == ExecutionErrorKind.Cancelled
                    ? Make(StepOutcome.Cancelled, "The request was cancelled.", error: error)
                    : Make(StepOutcome.TransportFailed, error.Message, error: error);
            }

            // No response means no assertions; an HTTP error status is still a response and its assertions run.
            StepCheckResult check;
            try
            {
                check = StepChecker.Check(request, response, redaction);
            }
            finally
            {
                ResponseSpool.Delete(response.SpoolPath);   // the run keeps no bodies, so a spooled one ends with its step
            }

            if (check.Outcome == StepOutcome.Passed && check.Extraction is { Succeeded: true } extraction)
            {
                foreach (var (name, value) in extraction.Values)
                {
                    runVariables[name] = value;
                    redaction.Add(value);
                }
            }

            var message = check.Outcome switch
            {
                StepOutcome.AssertionFailed => $"{check.Assertions.Count(a => !a.Result.Passed)} of {check.Assertions.Count} assertion(s) failed.",
                StepOutcome.ExtractionFailed => string.Join(" ", check.Extraction!.Failures),
                StepOutcome.Passed when check.HadNoAssertions => "No assertions defined: the request completed.",
                _ => null,
            };
            return Make(check.Outcome, message, response, check: check);
        }
        catch (OperationCanceledException)
        {
            return Make(StepOutcome.Cancelled, "The step was cancelled.");
        }
    }

    private static StepRecord Skipped(RequestDefinition request, int sequence, bool isCleanup, string message) => new()
    {
        StepId = Guid.NewGuid(),
        RequestId = request.Id,
        RequestName = request.Name,
        Sequence = sequence,
        IsCleanup = isCleanup,
        Outcome = StepOutcome.Skipped,
        Message = message,
        HadNoAssertions = request.Assertions.Count == 0,
    };

    private static string DescribeProgress(StepRecord step) =>
        $"{(step.IsCleanup ? "Cleanup " : string.Empty)}“{step.RequestName}”: {step.Outcome}";

    /// <summary>Identity aid built only from the secret-free structure of the collection and environment — never from resolved values.</summary>
    public static string ComputeSnapshotHash(RunConfiguration configuration)
    {
        var environment = configuration.Environment is null
            ? null
            : new
            {
                configuration.Environment.Name,
                // Variable names, plain values and which names are secrets; never secret values or ciphertext.
                Variables = configuration.Environment.Variables.Select(v => new { v.Name, v.Value, Secret = v.SecretId is not null }).ToList(),
            };
        var json = JsonSerializer.Serialize(new { configuration.Collection, Environment = environment, configuration.StopOnFailure }, HashOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..32];
    }
}

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using restByAno.Core.Contracts;
using restByAno.Core.Models;

namespace restByAno.Infrastructure.ImportExport;

/// <summary>
/// JSON run report (schemaVersion 1): run metadata and step results only. It never contains URLs, request/response headers or
/// bodies, extracted values, or assertion expected/actual values — those do not exist in <see cref="RunRecord"/>. Output is
/// bounded and written atomically when saved to a file.
/// </summary>
public sealed class JsonReportExporter : IReportExporter
{
    public const int SchemaVersion = 1;
    public const int MaxSteps = 10_000;
    public const int MaxMessageLength = 1_000;
    public const int MaxOutputBytes = 5 * 1024 * 1024;

    public async Task ExportAsync(RunRecord run, Stream destination, CancellationToken cancellationToken)
    {
        var bytes = Build(run);
        await destination.WriteAsync(bytes, cancellationToken);
    }

    public Task ExportFileAsync(RunRecord run, string path, CancellationToken cancellationToken) =>
        Task.Run(() => AtomicFile.WriteAllText(path, Encoding.UTF8.GetString(Build(run))), cancellationToken);

    public byte[] Build(RunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Steps.Count > MaxSteps)
            throw new InvalidOperationException($"The run has more than {MaxSteps:N0} steps; the report would exceed its size limit.");

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("schemaVersion", SchemaVersion);
            w.WriteString("kind", "runReport");
            w.WriteString("generatedUtc", DateTimeOffset.UtcNow);

            w.WriteStartObject("run");
            w.WriteString("id", run.Id);
            w.WriteString("collectionId", run.CollectionId);
            if (run.EnvironmentId is { } environmentId) w.WriteString("environmentId", environmentId); else w.WriteNull("environmentId");
            w.WriteString("startedUtc", run.StartedUtc);
            w.WriteString("endedUtc", run.EndedUtc);
            w.WriteNumber("durationMs", (long)(run.EndedUtc - run.StartedUtc).TotalMilliseconds);
            w.WriteString("outcome", run.Outcome.ToString());
            w.WriteString("snapshotHash", run.SnapshotHash);
            w.WriteStartObject("summary");
            foreach (var outcome in Enum.GetValues<StepOutcome>())
                w.WriteNumber(char.ToLowerInvariant(outcome.ToString()[0]) + outcome.ToString()[1..], run.Steps.Count(s => s.Outcome == outcome));
            w.WriteNumber("cleanupSteps", run.Steps.Count(s => s.IsCleanup));
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteStartArray("steps");
            foreach (var step in run.Steps)
            {
                w.WriteStartObject();
                w.WriteString("stepId", step.StepId);
                w.WriteString("requestId", step.RequestId);
                w.WriteString("requestName", Truncate(step.RequestName, 200));
                w.WriteNumber("sequence", step.Sequence);
                w.WriteBoolean("isCleanup", step.IsCleanup);
                w.WriteString("outcome", step.Outcome.ToString());
                if (step.StatusCode is { } status) w.WriteNumber("statusCode", status);
                if (step.ElapsedMilliseconds is { } elapsed) w.WriteNumber("elapsedMs", elapsed);
                if (step.BodyBytes is { } size) w.WriteNumber("bodyBytes", size);
                if (step.ErrorKind is { } kind) w.WriteString("errorKind", kind.ToString());
                if (step.Message is { } message) w.WriteString("message", Truncate(message, MaxMessageLength));
                w.WriteBoolean("hadNoAssertions", step.HadNoAssertions);
                w.WriteStartArray("assertions");
                foreach (var assertion in step.Assertions)
                {
                    w.WriteStartObject();
                    w.WriteString("assertionId", assertion.AssertionId);
                    w.WriteString("kind", assertion.Kind.ToString());
                    if (assertion.JsonPointer is { } pointer) w.WriteString("jsonPointer", pointer);
                    if (assertion.HeaderName is { } header) w.WriteString("headerName", header);
                    w.WriteBoolean("passed", assertion.Passed);
                    w.WriteString("reason", Truncate(assertion.Reason, MaxMessageLength));
                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        if (stream.Length > MaxOutputBytes)
            throw new InvalidOperationException("The report would exceed its size limit and was not written.");
        return stream.ToArray();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}

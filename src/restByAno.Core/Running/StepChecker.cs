using restByAno.Core.Assertions;
using restByAno.Core.Models;
using restByAno.Core.Resolution;

namespace restByAno.Core.Running;

/// <summary>Everything learned from checking one response against its request's rules.</summary>
public sealed record StepCheckResult(
    IReadOnlyList<AssertionEvaluation> Assertions, ExtractionResult? Extraction, StepOutcome Outcome)
{
    public bool HadNoAssertions => Assertions.Count == 0;
}

/// <summary>
/// Evaluates assertions, then — only when all of them passed — runs extraction, sharing one parsed JSON document.
/// Used by the standalone Send and by the collection runner so both behave identically.
/// </summary>
public static class StepChecker
{
    public static StepCheckResult Check(RequestDefinition request, ResponseSnapshot response, RedactionContext redaction)
    {
        using var body = new ResponseBodyJson(response);
        var assertions = AssertionEvaluator.Evaluate(request.Assertions, response, body, redaction);

        if (assertions.Any(a => !a.Result.Passed))
            return new StepCheckResult(assertions, null, StepOutcome.AssertionFailed);

        var extraction = ResponseExtractor.Extract(request.Extractors, body);
        return new StepCheckResult(assertions, extraction, extraction.Succeeded ? StepOutcome.Passed : StepOutcome.ExtractionFailed);
    }
}

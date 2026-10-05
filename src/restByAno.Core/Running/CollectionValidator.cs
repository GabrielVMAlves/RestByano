using restByAno.Core.Assertions;
using restByAno.Core.Models;
using restByAno.Core.Resolution;

namespace restByAno.Core.Running;

/// <summary>
/// Global validation before a run: catches invalid SAVED shapes only. Whether a variable is defined is NOT checked here —
/// an earlier step may extract it — so unresolved variables are reported per step when that step runs.
/// </summary>
public static class CollectionValidator
{
    public static IReadOnlyList<string> Validate(CollectionDefinition collection)
    {
        var errors = new List<string>();
        if (collection.Requests.Count == 0)
        {
            errors.Add("The collection has no requests, so there is nothing to run. Add at least one request.");
            return errors;
        }

        if (collection.Requests.All(r => r.IsCleanup))
            errors.Add("The collection has only cleanup requests. Add at least one regular request to run.");

        var folderIds = collection.Folders.Select(f => f.Id).ToHashSet();
        foreach (var request in collection.Requests)
        {
            var label = string.IsNullOrWhiteSpace(request.Name) ? "(unnamed request)" : $"“{request.Name}”";
            void Add(string message) => errors.Add($"{label}: {message}");

            if (string.IsNullOrWhiteSpace(request.Name)) Add("the request has no name.");
            if (string.IsNullOrWhiteSpace(request.UrlTemplate)) Add("the URL is empty.");
            if (!Enum.IsDefined(request.Method)) Add("the HTTP method is not valid.");
            if (request.FolderId is { } folder && !folderIds.Contains(folder)) Add("it refers to a folder that no longer exists.");

            if (request.Body.Kind == BodyKind.Json && RequestBuilder.JsonSyntaxError(request.Body.Content) is { } jsonError)
                Add(jsonError);

            ValidateAuth(request.Auth, Add);

            foreach (var assertion in request.Assertions)
                if (RuleValidator.ValidateAssertion(assertion) is { } assertionError)
                    Add($"assertion {assertion.Kind}: {assertionError}");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var extractor in request.Extractors)
            {
                if (RuleValidator.ValidateExtractor(extractor) is { } extractorError)
                    Add($"extractor “{extractor.VariableName}”: {extractorError}");
                else if (!seen.Add(extractor.VariableName))
                    Add($"extractor “{extractor.VariableName}” appears more than once.");
            }
        }

        return errors;
    }

    private static void ValidateAuth(AuthDefinition auth, Action<string> add)
    {
        void Need(string? value, string what)
        {
            if (!VariableNames.IsValid(value)) add($"authentication needs a valid variable name for the {what}.");
        }

        switch (auth.Kind)
        {
            case AuthKind.None:
                break;
            case AuthKind.Bearer:
                Need(auth.TokenVariable, "bearer token");
                break;
            case AuthKind.Basic:
                Need(auth.UsernameVariable, "user name");
                Need(auth.PasswordVariable, "password");
                break;
            case AuthKind.ApiKey:
                if (string.IsNullOrWhiteSpace(auth.ApiKeyName)) add("authentication needs the API key name.");
                Need(auth.ApiKeyVariable, "API key");
                if (auth.ApiKeyLocation is null) add("authentication needs the API key location.");
                break;
            default:
                add("the authentication type is not supported.");
                break;
        }
    }
}

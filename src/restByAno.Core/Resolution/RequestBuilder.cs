using System.Text;
using restByAno.Core.Models;

namespace restByAno.Core.Resolution;

public sealed record RequestBuildResult(ResolvedRequest? Request, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Request is not null;
}

/// <summary>
/// Turns an immutable <see cref="RequestDefinition"/> snapshot plus a <see cref="VariableSnapshot"/> into a memory-only
/// <see cref="ResolvedRequest"/>. Pure logic: no WPF, no I/O. Errors are user-facing and never echo variable values.
/// </summary>
public static class RequestBuilder
{
    private const string DefaultTextContentType = "text/plain; charset=utf-8";
    private const string DefaultJsonContentType = "application/json";

    private sealed record AuthApplication(HeaderEntry? Header, KeyValuePair<string, string>? Query)
    {
        public static AuthApplication None { get; } = new(null, null);
    }

    public static RequestBuildResult Build(RequestDefinition definition, TransportProfile transport, VariableSnapshot? variables = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(transport);
        variables ??= VariableSnapshot.Empty;

        var errors = new List<string>();
        var problems = new ResolutionProblems();

        var auth = ResolveAuth(definition.Auth, variables, problems, errors);
        var uri = BuildUri(definition, variables, problems, errors, auth.Query);

        var headers = new List<HeaderEntry>();
        var headerNumber = 0;
        foreach (var header in definition.Headers)
        {
            headerNumber++;
            if (!header.Enabled) continue;
            var name = TemplateResolver.ResolveText(header.Name, variables, problems).Trim();
            var value = TemplateResolver.ResolveText(header.Value, variables, problems);
            if (name.Length == 0)
            {
                errors.Add($"Header {headerNumber} is enabled but has no name.");
                continue;
            }

            if (name.Contains('\r') || name.Contains('\n') || value.Contains('\r') || value.Contains('\n'))
            {
                errors.Add($"Header {headerNumber} contains a line break after variables were substituted, which is not allowed.");
                continue;
            }

            headers.Add(new HeaderEntry(name, value));
        }

        // Authentication must not collide with something the user typed by hand.
        if (auth.Header is { } authHeader)
        {
            if (headers.Any(h => h.Name.Equals(authHeader.Name, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Authentication is configured and the request also has a “{authHeader.Name}” header. Remove one of them.");
            else
                headers.Add(authHeader);
        }

        byte[]? body = null;
        string? contentType = null;
        switch (definition.Body.Kind)
        {
            case BodyKind.None:
                break;
            case BodyKind.Text:
                body = Encoding.UTF8.GetBytes(TemplateResolver.ResolveText(definition.Body.Content, variables, problems));
                contentType = definition.Body.ContentType ?? DefaultTextContentType;
                break;
            case BodyKind.Json:
                if (string.IsNullOrWhiteSpace(definition.Body.Content))
                {
                    errors.Add("The JSON body is empty.");
                    break;
                }

                var json = TemplateResolver.ResolveJson(definition.Body.Content, variables, problems, out var jsonError);
                if (json is null)
                    errors.Add(jsonError!);
                else
                    body = Encoding.UTF8.GetBytes(json);
                contentType = definition.Body.ContentType ?? DefaultJsonContentType;
                break;
            default:
                errors.Add("The body type is not supported.");
                break;
        }

        errors.AddRange(problems.ToMessages());

        // A Content-Type header typed by the user takes precedence over the body type default.
        if (headers.Any(h => h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
            contentType = null;

        if (errors.Count > 0 || uri is null)
            return new RequestBuildResult(null, errors);

        return new RequestBuildResult(new ResolvedRequest
        {
            Method = definition.Method,
            Uri = uri,
            Headers = headers,
            HasBody = body is not null,
            Body = body ?? [],
            ContentType = body is null ? null : contentType,
            Transport = transport,
        }, []);
    }

    /// <summary>Validates JSON syntax of a body template only (no substitution). Safe, position-only message.</summary>
    public static string? JsonSyntaxError(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "The JSON body is empty.";
        TemplateResolver.ResolveJson(text, VariableSnapshot.Empty, new ResolutionProblems(), out var error);
        return error;
    }

    // ------------------------------------------------------------------ authentication

    private static AuthApplication ResolveAuth(AuthDefinition auth, VariableSnapshot variables, ResolutionProblems problems, List<string> errors)
    {
        switch (auth.Kind)
        {
            case AuthKind.None:
                return AuthApplication.None;

            case AuthKind.Bearer:
            {
                var token = AuthValue(auth.TokenVariable, "bearer token", variables, problems, errors);
                return token is null ? AuthApplication.None : new AuthApplication(new HeaderEntry("Authorization", $"Bearer {token}"), null);
            }

            case AuthKind.Basic:
            {
                var user = AuthValue(auth.UsernameVariable, "basic-auth user name", variables, problems, errors);
                var password = AuthValue(auth.PasswordVariable, "basic-auth password", variables, problems, errors);
                if (user is null || password is null) return AuthApplication.None;
                if (user.Contains(':'))
                {
                    errors.Add("A basic-auth user name cannot contain a colon.");
                    return AuthApplication.None;
                }

                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
                return new AuthApplication(new HeaderEntry("Authorization", $"Basic {encoded}"), null);
            }

            case AuthKind.ApiKey:
            {
                var key = auth.ApiKeyName?.Trim();
                if (string.IsNullOrEmpty(key))
                {
                    errors.Add("Enter the name of the API key (the header or query parameter name).");
                    return AuthApplication.None;
                }

                if (auth.ApiKeyLocation is null)
                {
                    errors.Add("Choose where the API key is sent: header or query.");
                    return AuthApplication.None;
                }

                var value = AuthValue(auth.ApiKeyVariable, "API key", variables, problems, errors);
                if (value is null) return AuthApplication.None;
                return auth.ApiKeyLocation == ApiKeyLocation.Header
                    ? new AuthApplication(new HeaderEntry(key, value), null)
                    : new AuthApplication(null, new KeyValuePair<string, string>(key, value));
            }

            default:
                errors.Add("The authentication type is not supported.");
                return AuthApplication.None;
        }
    }

    private static string? AuthValue(string? variableName, string what, VariableSnapshot variables, ResolutionProblems problems, List<string> errors)
    {
        var name = variableName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            errors.Add($"Choose the variable that holds the {what}.");
            return null;
        }

        if (!VariableNames.IsValid(name))
        {
            errors.Add($"“{name}” is not a valid variable name for the {what}.");
            return null;
        }

        if (!variables.TryGet(name, out var entry))
        {
            problems.AddMissing(name);
            return null;
        }

        if (entry.Status == VariableStatus.Unreadable || entry.Value is null)
        {
            problems.AddUnreadable(name);
            return null;
        }

        if (entry.Value.Length == 0)
        {
            errors.Add($"The variable “{name}” for the {what} is empty.");
            return null;
        }

        if (entry.Value.Contains('\r') || entry.Value.Contains('\n'))
        {
            errors.Add($"The variable “{name}” for the {what} contains a line break, which is not allowed.");
            return null;
        }

        return entry.Value;
    }

    // ------------------------------------------------------------------ URL

    private static Uri? BuildUri(
        RequestDefinition definition, VariableSnapshot variables, ResolutionProblems problems, List<string> errors,
        KeyValuePair<string, string>? authQuery)
    {
        var template = definition.UrlTemplate.Trim();
        if (template.Length == 0)
        {
            errors.Add("Enter a URL.");
            return null;
        }

        var url = TemplateResolver.ResolveText(template, variables, problems).Trim();
        if (problems.Any)
            return null; // the variable problems are reported once by the caller

        if (url.Contains('#'))
        {
            errors.Add("URLs with a fragment (#) are not supported. Remove the fragment.");
            return null;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("The URL must start with http:// or https:// (after variables are substituted).");
            return null;
        }

        // Existing query items stay exactly as typed; enabled rows are resolved first, then each name/value is encoded once.
        var builder = new StringBuilder(url);
        var usedNames = new HashSet<string>(ExistingQueryNames(url), StringComparer.OrdinalIgnoreCase);
        var rowNumber = 0;
        var rowsValid = true;
        foreach (var row in definition.QueryParameters)
        {
            rowNumber++;
            if (!row.Enabled) continue;
            var name = TemplateResolver.ResolveText(row.Name, variables, problems).Trim();
            var value = TemplateResolver.ResolveText(row.Value, variables, problems);
            if (name.Length == 0)
            {
                errors.Add($"Parameter {rowNumber} is enabled but has no name.");
                rowsValid = false;
                continue;
            }

            usedNames.Add(name);
            Append(builder, name, value);
        }

        if (authQuery is { } auth)
        {
            if (usedNames.Contains(auth.Key))
            {
                errors.Add($"Authentication sends the API key as the query parameter “{auth.Key}”, which the request already has. Remove one of them.");
                rowsValid = false;
            }
            else
            {
                Append(builder, auth.Key, auth.Value);
            }
        }

        if (!rowsValid || problems.Any) return null;

        if (!Uri.TryCreate(builder.ToString(), UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            errors.Add("The URL is not valid.");
            return null;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            errors.Add("URLs with embedded credentials are not allowed.");
            return null;
        }

        return uri;
    }

    private static void Append(StringBuilder builder, string name, string value)
    {
        var current = builder.ToString();
        if (!current.Contains('?')) builder.Append('?');
        else if (!current.EndsWith('?') && !current.EndsWith('&')) builder.Append('&');
        builder.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
    }

    private static IEnumerable<string> ExistingQueryNames(string url)
    {
        var index = url.IndexOf('?');
        if (index < 0 || index == url.Length - 1) yield break;
        foreach (var part in url[(index + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = part.Split('=', 2)[0];
            yield return Uri.UnescapeDataString(key);
        }
    }
}

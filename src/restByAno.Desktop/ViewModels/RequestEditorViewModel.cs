using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using restByAno.Core.Contracts;
using restByAno.Core.Models;
using restByAno.Core.Presentation;
using restByAno.Core.Resolution;
using restByAno.Core.Running;
using restByAno.Desktop.Services;
using restByAno.Infrastructure.Persistence;

namespace restByAno.Desktop.ViewModels;

public sealed partial class RequestEditorViewModel : ObservableObject
{
    private const string ScratchName = "Untitled request";

    private readonly IRequestExecutor _executor;
    private readonly ActiveEnvironmentViewModel _environment;
    private readonly VariableSnapshotFactory _variableSnapshots;
    private readonly SettingsService _settings;
    private readonly RunState _runState;
    private CancellationTokenSource? _sendCancellation;

    // Identity and the parts of a request this editor does not edit yet (kept so saving never drops them).
    private Guid _requestId = Guid.NewGuid();

    private string? _baselineJson;
    private bool _isNew;
    private bool _loading;

    public RequestEditorViewModel(
        IRequestExecutor executor, ResponseViewModel response, ActiveEnvironmentViewModel environment, VariableSnapshotFactory variableSnapshots,
        SettingsService settings, RunState runState)
    {
        _executor = executor;
        _environment = environment;
        _variableSnapshots = variableSnapshots;
        _settings = settings;
        _runState = runState;
        _runState.PropertyChanged += (_, _) => SendCommand.NotifyCanExecuteChanged();
        Response = response;
        Parameters.Changed += (_, _) => OnEdited();
        Headers.Changed += (_, _) => OnEdited();
        Assertions.Changed += (_, _) => OnEdited();
        Extractors.Changed += (_, _) => OnEdited();
    }

    public ResponseViewModel Response { get; }

    public KeyValueEditorViewModel Parameters { get; } = new("parameter");

    public KeyValueEditorViewModel Headers { get; } = new("header");

    public AssertionsEditorViewModel Assertions { get; } = new();

    public ExtractorsEditorViewModel Extractors { get; } = new();

    /// <summary>Problems in the assertion/extraction rules; they block saving and sending until fixed.</summary>
    public IReadOnlyList<string> RuleErrors => [.. Assertions.Validate(), .. Extractors.Validate()];

    public IReadOnlyList<ChoiceOption<HttpMethodKind>> MethodOptions { get; } =
    [
        new(HttpMethodKind.Get, "GET"),
        new(HttpMethodKind.Post, "POST"),
        new(HttpMethodKind.Put, "PUT"),
        new(HttpMethodKind.Patch, "PATCH"),
        new(HttpMethodKind.Delete, "DELETE"),
        new(HttpMethodKind.Head, "HEAD"),
        new(HttpMethodKind.Options, "OPTIONS"),
    ];

    public IReadOnlyList<ChoiceOption<AuthKind>> AuthKindOptions { get; } =
    [
        new(AuthKind.None, "None"),
        new(AuthKind.Bearer, "Bearer token"),
        new(AuthKind.Basic, "Basic"),
        new(AuthKind.ApiKey, "API key"),
    ];

    public IReadOnlyList<ChoiceOption<ApiKeyLocation>> ApiKeyLocationOptions { get; } =
    [
        new(ApiKeyLocation.Header, "Header"),
        new(ApiKeyLocation.Query, "Query parameter"),
    ];

    public IReadOnlyList<ChoiceOption<BodyKind>> BodyKindOptions { get; } =
    [
        new(BodyKind.None, "None"),
        new(BodyKind.Text, "Text"),
        new(BodyKind.Json, "JSON"),
    ];

    /// <summary>Set by the collection tree: the default variables of the collection that owns the open request.</summary>
    public Func<IReadOnlyList<VariableDefinition>> CollectionDefaults { get; set; } = () => [];

    /// <summary>Set by the collection tree: persists the open request. Returns true when saved.</summary>
    public Func<Task<bool>>? SaveHandler { get; set; }

    [ObservableProperty]
    private string _name = ScratchName;

    [ObservableProperty]
    private HttpMethodKind _method = HttpMethodKind.Get;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private BodyKind _bodyKind = BodyKind.None;

    [ObservableProperty]
    private string _bodyText = string.Empty;

    [ObservableProperty]
    private string _bodyMessage = string.Empty;

    [ObservableProperty]
    private bool _isCleanup;

    // Authentication references variables by name; the values live in the selected environment (secrets are encrypted there).
    [ObservableProperty]
    private AuthKind _authKind = AuthKind.None;

    [ObservableProperty]
    private string _tokenVariable = string.Empty;

    [ObservableProperty]
    private string _usernameVariable = string.Empty;

    [ObservableProperty]
    private string _passwordVariable = string.Empty;

    [ObservableProperty]
    private string _apiKeyName = string.Empty;

    [ObservableProperty]
    private string _apiKeyVariable = string.Empty;

    [ObservableProperty]
    private ApiKeyLocation _apiKeyIn = ApiKeyLocation.Header;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isSending;

    /// <summary>True when a request from a collection is open (otherwise the editor is a scratch pad that is never saved).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasOpenRequest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    /// <summary>"Collection / Folder" of the open request.</summary>
    [ObservableProperty]
    private string _locationText = "Scratch request — not saved to any collection";

    public string StateText => !HasOpenRequest ? string.Empty
        : _isNew ? "● Unsaved (new request)"
        : IsDirty ? "● Unsaved changes"
        : "✔ Saved";

    /// <summary>Immutable snapshot of what is currently typed; later edits cannot change a request in flight.</summary>
    public RequestDefinition ToDefinition() => new()
    {
        Id = _requestId,
        Name = Name,
        Method = Method,
        UrlTemplate = Url,
        QueryParameters = Parameters.ToEntries(),
        Headers = Headers.ToEntries(),
        Body = new BodyDefinition { Kind = BodyKind, Content = BodyText },
        Auth = BuildAuth(),
        Assertions = Assertions.ToDefinitions(),
        Extractors = Extractors.ToDefinitions(),
        IsCleanup = IsCleanup,
    };

    /// <summary>Opens a request from a collection. <paramref name="isNew"/> marks it as never saved (always dirty).</summary>
    public void Load(RequestDefinition definition, string location, bool isNew)
    {
        _loading = true;
        try
        {
            _requestId = definition.Id;
            AuthKind = definition.Auth.Kind;
            TokenVariable = definition.Auth.TokenVariable ?? string.Empty;
            UsernameVariable = definition.Auth.UsernameVariable ?? string.Empty;
            PasswordVariable = definition.Auth.PasswordVariable ?? string.Empty;
            ApiKeyName = definition.Auth.ApiKeyName ?? string.Empty;
            ApiKeyVariable = definition.Auth.ApiKeyVariable ?? string.Empty;
            ApiKeyIn = definition.Auth.ApiKeyLocation ?? ApiKeyLocation.Header;
            Assertions.Load(definition.Assertions);
            Extractors.Load(definition.Extractors);
            Name = definition.Name;
            Method = definition.Method;
            Url = definition.UrlTemplate;
            Parameters.Load(definition.QueryParameters);
            Headers.Load(definition.Headers);
            BodyKind = definition.Body.Kind;
            BodyText = definition.Body.Content;
            IsCleanup = definition.IsCleanup;
            BodyMessage = string.Empty;
            LocationText = location;
            _isNew = isNew;
            _baselineJson = isNew ? null : Fingerprint(definition);
            HasOpenRequest = true;
        }
        finally
        {
            _loading = false;
        }

        Response.Clear();
        Recompute();
    }

    /// <summary>Returns the editor to an empty scratch request (nothing open).</summary>
    public void ResetToScratch()
    {
        Load(new RequestDefinition { Id = Guid.NewGuid(), Name = ScratchName }, "Scratch request — not saved to any collection", isNew: false);
        HasOpenRequest = false;
        _baselineJson = null;
        Recompute();
    }

    /// <summary>Records that <paramref name="saved"/> is now what is persisted.</summary>
    public void MarkSaved(RequestDefinition saved)
    {
        _isNew = false;
        _baselineJson = Fingerprint(saved);
        Recompute();
    }

    /// <summary>Applies a rename done outside the editor without treating other pending edits as saved.</summary>
    public void ApplyRename(string newName)
    {
        var baseline = _baselineJson;
        _loading = true;
        try
        {
            Name = newName;
        }
        finally
        {
            _loading = false;
        }

        if (baseline is not null)
        {
            var current = JsonSerializer.Deserialize<RequestDefinition>(baseline, StorageJson.Options)!;
            _baselineJson = Fingerprint(current with { Name = newName });
        }

        Recompute();
    }

    /// <summary>Only the fields that belong to the selected authentication type are saved.</summary>
    private AuthDefinition BuildAuth() => AuthKind switch
    {
        AuthKind.Bearer => new AuthDefinition { Kind = AuthKind.Bearer, TokenVariable = TokenVariable.Trim() },
        AuthKind.Basic => new AuthDefinition { Kind = AuthKind.Basic, UsernameVariable = UsernameVariable.Trim(), PasswordVariable = PasswordVariable.Trim() },
        AuthKind.ApiKey => new AuthDefinition
        {
            Kind = AuthKind.ApiKey, ApiKeyName = ApiKeyName.Trim(), ApiKeyVariable = ApiKeyVariable.Trim(), ApiKeyLocation = ApiKeyIn,
        },
        _ => new AuthDefinition(),
    };

    private static string Fingerprint(RequestDefinition definition) =>
        JsonSerializer.Serialize(definition with { FolderId = null, Order = 0 }, StorageJson.Options);

    private void OnEdited()
    {
        if (!_loading) Recompute();
    }

    private void Recompute()
    {
        IsDirty = HasOpenRequest && (_isNew || _baselineJson != Fingerprint(ToDefinition()));
        OnPropertyChanged(nameof(StateText));
    }

    partial void OnNameChanged(string value) => OnEdited();
    partial void OnMethodChanged(HttpMethodKind value) => OnEdited();
    partial void OnUrlChanged(string value) => OnEdited();
    partial void OnBodyKindChanged(BodyKind value)
    {
        BodyMessage = string.Empty;
        OnEdited();
    }

    partial void OnBodyTextChanged(string value) => OnEdited();
    partial void OnIsCleanupChanged(bool value) => OnEdited();
    partial void OnAuthKindChanged(AuthKind value) => OnEdited();
    partial void OnTokenVariableChanged(string value) => OnEdited();
    partial void OnUsernameVariableChanged(string value) => OnEdited();
    partial void OnPasswordVariableChanged(string value) => OnEdited();
    partial void OnApiKeyNameChanged(string value) => OnEdited();
    partial void OnApiKeyVariableChanged(string value) => OnEdited();
    partial void OnApiKeyInChanged(ApiKeyLocation value) => OnEdited();

    // One request or one collection run at a time: the standalone Send is disabled while a run is active.
    private bool CanSend() => !IsSending && !_runState.IsRunning;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (IsSending) return;

        Response.Clear();
        var ruleErrors = RuleErrors;
        if (ruleErrors.Count > 0)
        {
            Response.ShowValidation(["Fix the assertion/extraction rules before sending:", .. ruleErrors]);
            return;
        }

        var snapshot = ToDefinition();           // immutable copy of what is on screen right now
        var defaults = CollectionDefaults();

        using var cancellation = new CancellationTokenSource();
        _sendCancellation = cancellation;
        IsSending = true;
        Response.BeginSending();
        try
        {
            // Variables are frozen here: later edits of the environment or collection affect only future sends.
            var environment = await _environment.LoadActiveAsync(cancellation.Token);
            var variables = await _variableSnapshots.BuildAsync(defaults, environment, null, cancellation.Token);

            // Settings and the environment's transport profile are read now: later changes apply to future sends only.
            var transport = TransportProfile.ForSend(_settings.Current.TimeoutSeconds, environment?.TransportProfile);
            var build = RequestBuilder.Build(snapshot, transport, variables);
            if (build.Request is null)
            {
                Response.ShowValidation(build.Errors);
                return;
            }

            var result = await _executor.ExecuteAsync(build.Request, cancellation.Token);
            await Response.ShowResultAsync(result);

            // Assertions and extraction run on the response; the redaction context hides secret values in on-screen details.
            if (result.Response is { } response)
            {
                var redaction = new RedactionContext();
                redaction.Add(variables.SecretValues);
                Response.ShowChecks(StepChecker.Check(snapshot, response, redaction), snapshot);
            }
            else
            {
                Response.ShowChecksNotEvaluated("There was no usable response, so assertions and extraction were not evaluated.");
            }
        }
        catch (OperationCanceledException)
        {
            Response.ShowValidation(["The request was cancelled before it was sent."]);
        }
        catch (StorageException ex)
        {
            Response.ShowValidation([ex.Problem.Message]);
        }
        catch (Exception ex)
        {
            // The executor reports expected failures as results; this is a defect guard, not control flow.
            Response.ShowUnexpectedError($"An unexpected error occurred ({ex.GetType().Name}).");
        }
        finally
        {
            _sendCancellation = null;
            IsSending = false;
        }
    }

    private bool CanCancel() => IsSending;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _sendCancellation?.Cancel();

    private bool CanSave() => HasOpenRequest && IsDirty;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (SaveHandler is { } handler)
            await handler();
    }

    [RelayCommand]
    private void FormatJson()
    {
        BodyMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(BodyText))
        {
            BodyMessage = "There is no JSON to format.";
            return;
        }

        if (JsonText.TryFormat(BodyText, out var formatted, out var error))
            BodyText = formatted;
        else
            BodyMessage = $"The body is {error}. It was left unchanged.";
    }
}

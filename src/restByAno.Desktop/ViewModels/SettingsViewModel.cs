using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using restByAno.Core.Contracts;
using restByAno.Core.Models;
using restByAno.Desktop.Services;
using restByAno.Infrastructure.Configuration;
using restByAno.Infrastructure.Persistence;
using restByAno.Infrastructure.Policy;

namespace restByAno.Desktop.ViewModels;

/// <summary>
/// Global limits (timeout, history retention) and the transport profile of the active environment
/// (proxy mode/address and the non-loopback-HTTP switch). Changes affect future sends only. The managed target policy is
/// shown read-only: restByAno never writes it.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IEnvironmentRepository _environments;
    private readonly IDialogService _dialogs;
    private EnvironmentDefinition? _activeEnvironment;

    public SettingsViewModel(
        SettingsService settings, IEnvironmentRepository environments, ActiveEnvironmentViewModel activeEnvironment,
        ManagedTargetPolicy policy, StorageOptions storage, IDialogService dialogs)
    {
        _settings = settings;
        _environments = environments;
        _dialogs = dialogs;
        ActiveEnvironmentId = activeEnvironment.SelectedId;
        ActiveEnvironmentName = activeEnvironment.Selected.Name;
        DataDirectory = storage.DataDirectory;
        DataDirectoryHelp =
            $"To keep restByAno's data in another folder, start it with:{Environment.NewLine}restByAno.exe --data-dir \"D:\\path\\to\\folder\"{Environment.NewLine}" +
            "Use the same argument every time you start it. Changing the folder while the app is running is not supported.";

        var current = settings.Current;
        _timeoutSeconds = current.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        _retentionDays = current.RetentionDays.ToString(CultureInfo.InvariantCulture);

        var status = policy.GetStatus();
        PolicyPath = status.Path;
        PolicySummary = status.Mode switch
        {
            PolicyMode.Active => $"Managed target policy active: {status.Targets.Count} allowed target(s).",
            PolicyMode.Invalid => $"Managed target policy is INVALID — all destinations are blocked. {status.Message}",
            _ => "No target policy file found; destinations are not restricted. To restrict them, place a target-policy.json in C:\temp or start with --target-policy <path>.",
        };
        PolicyTargets = status.Targets.Select(t => $"{t.Scheme}://{t.Host}:{t.Port}").ToList();
    }

    public string DataDirectory { get; }
    public string DataDirectoryHelp { get; }
    public string PolicyPath { get; }
    public string PolicySummary { get; }
    public IReadOnlyList<string> PolicyTargets { get; }

    public Guid? ActiveEnvironmentId { get; }
    public string ActiveEnvironmentName { get; }
    public bool HasActiveEnvironment => ActiveEnvironmentId is not null;

    public IReadOnlyList<ChoiceOption<ProxyMode>> ProxyModeOptions { get; } =
    [
        new(ProxyMode.System, "System proxy (default)"),
        new(ProxyMode.Explicit, "Explicit proxy address"),
    ];

    [ObservableProperty]
    private string _timeoutSeconds;

    [ObservableProperty]
    private string _retentionDays;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExplicitProxy))]
    private ProxyMode _proxyMode = ProxyMode.System;

    [ObservableProperty]
    private string _proxyUri = string.Empty;

    [ObservableProperty]
    private bool _allowNonLoopbackHttp;

    [ObservableProperty]
    private string _validationText = string.Empty;

    [ObservableProperty]
    private string _savedText = string.Empty;

    public bool IsExplicitProxy => ProxyMode == ProxyMode.Explicit;

    public async Task InitializeAsync()
    {
        if (ActiveEnvironmentId is not { } id) return;
        try
        {
            _activeEnvironment = await _environments.LoadAsync(id, default);
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
        }

        if (_activeEnvironment is { } environment)
        {
            ProxyMode = environment.TransportProfile.ProxyMode;
            ProxyUri = environment.TransportProfile.ProxyUri ?? string.Empty;
            AllowNonLoopbackHttp = environment.TransportProfile.AllowNonLoopbackHttp;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        SavedText = string.Empty;
        var errors = new List<string>();

        var timeout = ParseInt(TimeoutSeconds, "Timeout", errors);
        var retention = ParseInt(RetentionDays, "History retention", errors);

        var candidate = new AppSettings
        {
            TimeoutSeconds = timeout ?? 0,
            RetentionDays = retention ?? 0,
        };
        if (timeout is not null && candidate.TimeoutSeconds is < AppSettings.MinTimeoutSeconds or > AppSettings.MaxTimeoutSeconds)
            errors.Add($"Timeout must be between {AppSettings.MinTimeoutSeconds} and {AppSettings.MaxTimeoutSeconds} seconds.");
        if (retention is not null && candidate.RetentionDays is < AppSettings.MinRetentionDays or > AppSettings.MaxRetentionDays)
            errors.Add($"History retention must be between {AppSettings.MinRetentionDays} and {AppSettings.MaxRetentionDays} days.");

        if (HasActiveEnvironment && ProxyMode == ProxyMode.Explicit)
        {
            if (!Uri.TryCreate(ProxyUri.Trim(), UriKind.Absolute, out var proxy)
                || (proxy.Scheme != Uri.UriSchemeHttp && proxy.Scheme != Uri.UriSchemeHttps))
                errors.Add("The proxy address must be an absolute http or https URL, for example http://proxy.company.example:8080.");
        }

        ValidationText = string.Join(Environment.NewLine, errors);
        if (errors.Count > 0) return;

        try
        {
            _settings.Update(s => s with
            {
                TimeoutSeconds = candidate.TimeoutSeconds,
                RetentionDays = candidate.RetentionDays,
            });

            if (_activeEnvironment is { } environment)
            {
                var profile = environment.TransportProfile with
                {
                    ProxyMode = ProxyMode,
                    ProxyUri = ProxyMode == ProxyMode.Explicit ? ProxyUri.Trim() : null,
                    AllowNonLoopbackHttp = AllowNonLoopbackHttp,
                };
                _activeEnvironment = environment with { TransportProfile = profile };
                await _environments.SaveAsync(_activeEnvironment, default);
            }
        }
        catch (StorageException ex)
        {
            _dialogs.ShowMessage(ex.Problem.Title, ex.Problem.Message);
            return;
        }

        SavedText = "✔ Saved. Changes apply to the next request; a request already in flight keeps its settings.";
    }

    private static int? ParseInt(string text, string label, List<string> errors)
    {
        if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return value;
        errors.Add($"{label} must be a whole number.");
        return null;
    }
}

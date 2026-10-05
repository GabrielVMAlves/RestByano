using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;

namespace restByAno.Desktop.Services;

/// <summary>
/// Keeps the dark theme tokens in sync with Windows high contrast. In high contrast the token brushes are
/// replaced by system brushes in Application.Resources; leaving high contrast restores the dark tokens.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private static readonly (string Token, ResourceKey SystemKey)[] HighContrastMap =
    [
        ("BackgroundBrush", SystemColors.WindowBrushKey),
        ("PanelBrush", SystemColors.WindowBrushKey),
        ("RaisedBrush", SystemColors.WindowBrushKey),
        ("TextBrush", SystemColors.WindowTextBrushKey),
        ("SecondaryTextBrush", SystemColors.WindowTextBrushKey),
        ("LineBrush", SystemColors.WindowTextBrushKey),
        ("AccentBrush", SystemColors.HotTrackBrushKey),
        ("AccentTextBrush", SystemColors.WindowBrushKey),
        ("AccentHoverBrush", SystemColors.HighlightBrushKey),
        ("HoverBrush", SystemColors.WindowBrushKey),
        ("PressedBrush", SystemColors.WindowBrushKey),
        ("SelectedBrush", SystemColors.HighlightBrushKey),
        ("SelectedTextBrush", SystemColors.HighlightTextBrushKey),
        ("DisabledTextBrush", SystemColors.GrayTextBrushKey),
        ("DisabledBackgroundBrush", SystemColors.WindowBrushKey),
        ("SuccessBrush", SystemColors.WindowTextBrushKey),
        ("WarningBrush", SystemColors.WindowTextBrushKey),
        ("ErrorBrush", SystemColors.WindowTextBrushKey),
    ];

    private bool _started;

    public void Start()
    {
        if (_started) return;
        _started = true;
        Apply();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Dispose()
    {
        if (!_started) return;
        _started = false;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Reapply();
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Covers switching between high contrast themes while high contrast stays on.
        if (e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility)
            Reapply();
    }

    private static void Reapply()
    {
        var app = Application.Current;
        app?.Dispatcher.BeginInvoke(Apply, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void Apply()
    {
        var app = Application.Current;
        var resources = app.Resources;
        foreach (var (token, systemKey) in HighContrastMap)
        {
            if (SystemParameters.HighContrast)
                resources[token] = app.FindResource(systemKey);
            else
                resources.Remove(token);
        }
    }
}

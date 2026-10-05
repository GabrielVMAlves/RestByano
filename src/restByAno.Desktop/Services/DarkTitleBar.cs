using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace restByAno.Desktop.Services;

/// <summary>
/// Asks Windows to draw the native title bar of an app-owned window in dark mode.
/// The title bar remains OS-controlled; this is a request, not a guarantee on older Windows builds.
/// </summary>
public static class DarkTitleBar
{
    private const int DwmUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Set(window);
        else
            window.SourceInitialized += (_, _) => Set(window);
    }

    private static void Set(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var enabled = SystemParameters.HighContrast ? 0 : 1;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }
}

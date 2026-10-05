using System.Runtime.InteropServices;
using System.Windows;

namespace restByAno.Desktop.Services;

/// <summary>Copies text only on an explicit user action. The clipboard may be briefly locked by other apps.</summary>
public interface IClipboardService
{
    bool TrySetText(string text);
}

public sealed class WpfClipboardService : IClipboardService
{
    public bool TrySetText(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
        }

        return false;
    }
}

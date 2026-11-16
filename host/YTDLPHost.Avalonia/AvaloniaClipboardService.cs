using Avalonia.Controls;
using Avalonia.Input.Platform;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

/// <summary>
/// Puts text on the clipboard through the main window. Avalonia 12 reads the clipboard through the window (a TopLevel); with no window showing there is
/// nothing to ask, so the request is logged and dropped. Never throws.
/// </summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    private readonly Func<Window?> _window;

    public AvaloniaClipboardService(Func<Window?> window) => _window = window;

    public void SetText(string text)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(_window())?.Clipboard;
            if (clipboard == null)
            {
                AppLogger.Log("[CLIPBOARD] No window to reach the clipboard through; text not copied.");
                return;
            }
            _ = SetAsync(clipboard, text);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[CLIPBOARD ERROR] {ex.Message}");
        }
    }

    private static async Task SetAsync(IClipboard clipboard, string text)
    {
        try { await clipboard.SetTextAsync(text); }
        catch (Exception ex) { AppLogger.Log($"[CLIPBOARD ERROR] {ex.Message}"); }
    }
}

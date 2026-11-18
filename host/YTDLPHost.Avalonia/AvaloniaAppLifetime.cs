using Avalonia.Controls;
using Avalonia.Threading;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

/// <summary>
/// App-level operations for the shared view model. "Minimize" means hide to the tray: the Avalonia app is a tray app whose window is hidden,
/// not parked in the task bar. Where the desktop has no tray (<see cref="TraySupport"/>, step 4.5) it minimises the window instead.
/// </summary>
public sealed class AvaloniaAppLifetime : IAppLifetime
{
    private readonly Func<Window?> _window;
    private readonly Action _shutdown;

    public AvaloniaAppLifetime(Func<Window?> window, Action shutdown)
    {
        _window = window;
        _shutdown = shutdown;
        Dispatcher.UIThread.UnhandledException += (_, e) => UiUnhandledException?.Invoke(e.Exception);
    }

    public event Action<Exception>? UiUnhandledException;

    public void MinimizeMainWindow() => Dispatcher.UIThread.Post(() =>
    {
        var window = _window();
        if (window == null) return;
        if (TraySupport.CanHideToTray) window.Hide();
        else window.WindowState = WindowState.Minimized;
    });

    public void Shutdown() => _shutdown();
}

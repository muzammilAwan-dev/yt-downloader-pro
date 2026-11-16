using Avalonia.Controls;
using Avalonia.Threading;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

/// <summary>
/// App-level operations for the shared view model. "Minimize" means hide to the tray here: the Avalonia app is a tray app whose window is hidden,
/// not parked in the task bar.
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

    public void MinimizeMainWindow() => Dispatcher.UIThread.Post(() => _window()?.Hide());

    public void Shutdown() => _shutdown();
}

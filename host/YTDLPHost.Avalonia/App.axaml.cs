using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

/// <summary>
/// The app object. A tray app: closing the window only hides it, and the process ends only through Exit (explicit shutdown).
/// The window is created on first use, so "--hidden" never shows one at all.
/// </summary>
public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _window;
    private PlatformServices? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _services = AvaloniaPlatform.Create(this, () => _window, Exit);
            _services.Tray.ShowWindowRequested += (_, _) => ShowMainWindow();
            _services.Tray.ExitRequested += (_, _) => Exit();
            _services.Tray.Initialize();
            _services.Notifier.Activated += (_, _) => ShowMainWindow();

            var args = desktop.Args ?? Array.Empty<string>();
            AppLogger.Log($"[AVALONIA] Started. Hidden start: {HostCommandLine.StartHidden(args)}.");
            if (!HostCommandLine.StartHidden(args)) ShowMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainWindow()
    {
        _window ??= new MainWindow(_services!, Exit);
        if (!_window.IsVisible) _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Exit()
    {
        if (_window != null) _window.AllowClose = true;
        _services?.Tray.Dispose();
        _desktop?.Shutdown();
    }
}

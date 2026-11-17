using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

namespace YTDLPHost.UI;

/// <summary>
/// The app object. A tray app: closing the window only hides it, and the process ends only through Exit (explicit shutdown).
/// The shared <see cref="MainViewModel"/> exists from the start (downloads must run with no window); the window is created on first use,
/// so "--hidden" never shows one at all.
/// </summary>
public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _window;
    private PlatformServices? _services;
    private MainViewModel? _viewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => _viewModel?.Dispose();

            _services = AvaloniaPlatform.Create(this, () => _window, Exit);

            // The view model creates the tray icon and listens to the tray and notification events itself (as in the WPF app);
            // all the app does is show the window when it asks to.
            _viewModel = new MainViewModel(_services);
            _viewModel.RequestShowWindow += (_, _) => Dispatcher.UIThread.Post(ShowMainWindow);

            var args = desktop.Args ?? Array.Empty<string>();
            AppLogger.Log($"[AVALONIA] Started. Hidden start: {HostCommandLine.StartHidden(args)}.");
            if (!HostCommandLine.StartHidden(args)) ShowMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainWindow()
    {
        _window ??= new MainWindow(_viewModel!);
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

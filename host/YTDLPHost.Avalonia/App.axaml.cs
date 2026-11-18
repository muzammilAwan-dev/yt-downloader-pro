using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using YTDLPHost.Models;
using YTDLPHost.Native;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

namespace YTDLPHost.UI;

/// <summary>
/// The app object, with the start-up of the WPF app (App.xaml.cs) ported: one instance per user (a second launch hands its link over and ends),
/// the browsers linked to this install, the Native Messaging pipe, the payload folder of the ytdlp:// route, and a tray app lifecycle
/// (closing the window only hides it; the process ends through Exit). The shared <see cref="MainViewModel"/> exists from the start, because downloads
/// must run with no window; the window is created on first use, so "--hidden" never shows one.
/// </summary>
public partial class App : Application
{
    private const string ShowUrl = "ytdlp://show";

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _window;
    private PlatformServices? _services;
    private MainViewModel? _viewModel;
    private SingleInstance? _singleInstance;
    private NativePipeServer? _nativeServer;
    private FileSystemWatcher? _payloadWatcher;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            var args = desktop.Args ?? Array.Empty<string>();
            string? urlArg = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));

            AppLogger.Log("=====================================");
            AppLogger.Log("=== YTDLP HOST APPLICATION START (Avalonia) ===");
            AppLogger.Log($"OS Version: {Environment.OSVersion}");
            AppLogger.Log(string.IsNullOrEmpty(urlArg) ? "[BOOT] Launched normally (no link argument)." : $"[BOOT] Launch argument received: {urlArg}");

            string payloadsDir = AppPaths.Current.PayloadsDir;
            Directory.CreateDirectory(payloadsDir);

            _singleInstance = SingleInstance.ForCurrentUser(AppPaths.Current);
            if (!_singleInstance.TryBecomePrimary())
            {
                HandOverToRunningInstance(urlArg, payloadsDir);   // ends the process
                return;
            }

            AppLogger.Log("[BOOT] Primary instance established. Initializing UI components.");
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => Cleanup();

            _services = AvaloniaPlatform.Create(this, () => _window, Exit);

            foreach (var line in PlatformNativeHost.EnsureRegistered()) AppLogger.Log("[NATIVE HOST] " + line);

            // A second launch (or a click on the app icon again) only asks the first one to show itself.
            _singleInstance.MessageReceived += (_, _) =>
            {
                AppLogger.Log("[NAMED PIPE] Primary instance woke up via pipe.");
                Dispatcher.UIThread.Post(ShowMainWindow);
            };

            // The view model creates the tray icon and listens to the tray and notification events itself (as in the WPF app);
            // all the app does is show the window when it asks to.
            _viewModel = new MainViewModel(_services);
            _viewModel.RequestShowWindow += (_, _) => Dispatcher.UIThread.Post(ShowMainWindow);

            // Native Messaging: the shim of the browser extension connects here. The ytdlp:// route below keeps working.
            _nativeServer = new NativePipeServer(
                NativePipe.NameForCurrentUser(),
                _viewModel.NativeSink,
                () => new MapperSettings(AppSettings.Load().DownloadFolder),
                Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");
            _nativeServer.Start();

            _payloadWatcher = new FileSystemWatcher(payloadsDir, "*.url") { EnableRaisingEvents = true };
            _payloadWatcher.Created += (_, e) => ProcessPayloadFile(e.FullPath);
            foreach (var file in Directory.GetFiles(payloadsDir, "*.url")) ProcessPayloadFile(file);

            bool startHidden = HostCommandLine.StartHidden(args);
            AppLogger.Log($"[AVALONIA] Started. Hidden start: {startHidden}.");
            if (!startHidden) ShowMainWindow();

            if (!string.IsNullOrEmpty(urlArg) && urlArg.StartsWith("ytdlp://", StringComparison.Ordinal) && urlArg != ShowUrl)
            {
                _viewModel.ProcessUrl(urlArg);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The second process: leaves its link as a file for the first one (the file route survives a first process that is busy), asks it to show itself, and ends.</summary>
    private static void HandOverToRunningInstance(string? urlArg, string payloadsDir)
    {
        AppLogger.Log("[BOOT] Secondary instance detected. Routing payload...");
        try
        {
            string url = urlArg ?? ShowUrl;
            if (url != ShowUrl)
            {
                File.WriteAllText(Path.Combine(payloadsDir, Guid.NewGuid() + ".url"), url);
                AppLogger.Log("[BOOT] Payload saved to disk queue.");
            }

            // On a pool thread: waiting for an async call on the UI thread, whose synchronization context the call would need to continue, hangs.
            Task.Run(() => SingleInstance.SendToRunningInstanceAsync(ShowUrl)).GetAwaiter().GetResult();
            Thread.Sleep(500);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[BOOT] Hand-over failed: {ex.Message}");
        }
        Environment.Exit(0);
    }

    private void ProcessPayloadFile(string filePath)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(100);
                string url = File.ReadAllText(filePath);
                File.Delete(filePath);

                AppLogger.Log("[FILE IPC] Primary instance successfully extracted payload from disk.");
                Dispatcher.UIThread.Post(() =>
                {
                    if (url != ShowUrl && url.StartsWith("ytdlp://", StringComparison.Ordinal)) _viewModel?.ProcessUrl(url);
                    ShowMainWindow();
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[FILE IPC ERROR] {ex.Message}");
            }
        });
    }

    private void ShowMainWindow()
    {
        if (_viewModel == null) return;
        _window ??= new MainWindow(_viewModel);
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

    private void Cleanup()
    {
        _payloadWatcher?.Dispose();
        _nativeServer?.Dispose();
        _viewModel?.Dispose();
        _singleInstance?.Dispose();
    }
}

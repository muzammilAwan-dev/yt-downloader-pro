using System.Diagnostics;
using YTDLPHost.Services;

namespace YTDLPHost.Wpf
{
    // WPF implementations of the Core UI interfaces. Fully-qualified System.Windows.* names are
    // deliberate: UseWindowsForms also brings System.Windows.Forms into scope, which has its own
    // Application and MessageBox types. These classes are replaced wholesale by Avalonia versions in Phase 4.

    public sealed class WpfUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) =>
            System.Windows.Application.Current?.Dispatcher.Invoke(action);

        public void Post(Action action) =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);

        public void Post(UiPriority priority, Action action) =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                priority == UiPriority.Background
                    ? System.Windows.Threading.DispatcherPriority.Background
                    : System.Windows.Threading.DispatcherPriority.Normal,
                action);
    }

    public sealed class WpfDialogService : IDialogService
    {
        public void Show(string title, string message, DialogKind kind = DialogKind.Info)
        {
            var image = kind switch
            {
                DialogKind.Error => System.Windows.MessageBoxImage.Error,
                DialogKind.Warning => System.Windows.MessageBoxImage.Warning,
                _ => System.Windows.MessageBoxImage.Information
            };
            System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, image);
        }
    }

    public sealed class WpfClipboardService : IClipboardService
    {
        // SetDataObject(copy: true) keeps the text after the app exits. The clipboard can be locked by another
        // application for a moment (COMException); that must never crash the UI.
        public void SetText(string text)
        {
            try { System.Windows.Clipboard.SetDataObject(text, true); } catch { }
        }
    }

    public sealed class WindowsShellActions : IShellActions
    {
        public void RevealFile(string path) =>
            Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true });

        public void OpenFolder(string path) =>
            Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

        public void OpenFile(string path) =>
            Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

        // A missing file association or a vanished folder must not crash the app: log it and carry on.
        private static void Start(ProcessStartInfo psi)
        {
            try { Process.Start(psi); }
            catch (Exception ex) { AppLogger.Log($"[SHELL ERROR] Could not open '{psi.FileName} {psi.Arguments}': {ex.Message}"); }
        }
    }

    /// <summary>The one place that decides which implementations the WPF app uses. Avalonia gets its own in Phase 4.</summary>
    public static class WpfPlatform
    {
        public static PlatformServices Create()
        {
            var tray = new TrayIconService(); // implements both the tray and the notification interface
            return new PlatformServices(
                new WpfUiDispatcher(),
                new WpfDialogService(),
                new WpfAppLifetime(),
                new WpfClipboardService(),
                tray,
                tray,
                new WindowsShellActions());
        }
    }

    public sealed class WpfAppLifetime : IAppLifetime
    {
        public event Action<Exception>? UiUnhandledException;

        public WpfAppLifetime()
        {
            var app = System.Windows.Application.Current;
            if (app != null)
                app.DispatcherUnhandledException += (s, e) => UiUnhandledException?.Invoke(e.Exception);
        }

        public void MinimizeMainWindow()
        {
            var window = System.Windows.Application.Current?.MainWindow;
            if (window != null) window.WindowState = System.Windows.WindowState.Minimized;
        }

        public void Shutdown() => System.Windows.Application.Current?.Shutdown();
    }
}

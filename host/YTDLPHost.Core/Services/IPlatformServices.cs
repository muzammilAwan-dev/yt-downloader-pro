namespace YTDLPHost.Services
{
    /// <summary>
    /// Opens things in the operating system's own tools. Replaces direct Process.Start / explorer.exe calls so the
    /// logic that decides WHAT to open stays in Core. Implementations must not throw: a failure is logged, never a crash.
    /// </summary>
    public interface IShellActions
    {
        /// <summary>Shows the file selected in the system file manager (Explorer, Finder, ...).</summary>
        void RevealFile(string path);

        /// <summary>Opens a folder in the system file manager.</summary>
        void OpenFolder(string path);

        /// <summary>Opens a file with its default application.</summary>
        void OpenFile(string path);
    }

    /// <summary>The system tray / menu-bar icon: hover text, a small menu, and its two requests.</summary>
    public interface ITrayService : IDisposable
    {
        /// <summary>The user asked to see the main window (menu item or click on the icon).</summary>
        event EventHandler? ShowWindowRequested;

        /// <summary>The user chose Exit in the tray menu.</summary>
        event EventHandler? ExitRequested;

        /// <summary>Creates the icon. Call once, after subscribing to the events.</summary>
        void Initialize();

        void UpdateTooltip(string text);
    }

    /// <summary>
    /// Desktop notifications. Kept separate from the tray icon because on Linux and macOS they are different
    /// mechanisms; the Windows implementation happens to be one class.
    /// </summary>
    public interface INotifier
    {
        /// <summary>The user clicked a notification (the app should show its main window).</summary>
        event EventHandler? Activated;

        void NotifyDownloadComplete(string fileName);
    }

    /// <summary>
    /// Everything the view model needs from the host platform, built in one place per UI toolkit
    /// (WPF today, Avalonia in Phase 4) so swapping the toolkit does not touch the view model.
    /// </summary>
    public sealed record PlatformServices(
        IUiDispatcher Ui,
        IDialogService Dialogs,
        IAppLifetime Lifetime,
        IClipboardService Clipboard,
        ITrayService Tray,
        INotifier Notifier,
        IShellActions Shell);
}

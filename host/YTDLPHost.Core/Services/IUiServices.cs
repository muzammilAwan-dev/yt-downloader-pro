namespace YTDLPHost.Services
{
    public enum UiPriority { Normal, Background }

    /// <summary>
    /// Runs work on the UI thread. Replaces direct Application.Current.Dispatcher calls so logic that
    /// lives in Core (and later Avalonia, where the dispatcher is Dispatcher.UIThread) never touches WPF.
    /// Both methods are no-ops when the UI is already gone (during shutdown).
    /// </summary>
    public interface IUiDispatcher
    {
        /// <summary>Runs <paramref name="action"/> on the UI thread and waits for it to finish.</summary>
        void Invoke(Action action);

        /// <summary>Queues <paramref name="action"/> on the UI thread and returns immediately.</summary>
        void Post(Action action);

        /// <summary>Queues <paramref name="action"/> at the given priority and returns immediately.</summary>
        void Post(UiPriority priority, Action action);
    }

    public enum DialogKind { Info, Warning, Error }

    /// <summary>Shows a modal message to the user. Replaces direct MessageBox calls.</summary>
    public interface IDialogService
    {
        void Show(string title, string message, DialogKind kind = DialogKind.Info);
    }

    /// <summary>
    /// App-level lifetime and window operations the view model needs but must not implement itself.
    /// </summary>
    public interface IAppLifetime
    {
        /// <summary>Raised for an unhandled exception on the UI thread (used for the crash log).</summary>
        event Action<Exception>? UiUnhandledException;

        /// <summary>Minimizes the main window (used when hiding to the tray).</summary>
        void MinimizeMainWindow();

        /// <summary>Terminates the application.</summary>
        void Shutdown();
    }
}

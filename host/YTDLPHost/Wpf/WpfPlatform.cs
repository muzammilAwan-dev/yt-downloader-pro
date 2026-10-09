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

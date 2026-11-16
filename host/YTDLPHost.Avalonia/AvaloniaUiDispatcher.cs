using Avalonia.Threading;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

/// <summary>Runs work on Avalonia's UI thread (the counterpart of the WPF app's dispatcher class).</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Invoke(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Invoke(action);
    }

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public void Post(UiPriority priority, Action action) =>
        Dispatcher.UIThread.Post(action, priority == UiPriority.Background ? DispatcherPriority.Background : DispatcherPriority.Normal);
}

using YTDLPHost.Services;

namespace YTDLPHost.UI;

/// <summary>Stand-in until step 4.2 chooses and wires a real cross-platform notification library: it only writes to the log.</summary>
public sealed class LoggingNotifier : INotifier
{
#pragma warning disable CS0067   // raised by the real notifier of step 4.2
    public event EventHandler? Activated;
#pragma warning restore CS0067

    public void NotifyDownloadComplete(string fileName) => AppLogger.Log($"[NOTIFY] Download complete: {fileName}");

    public void NotifyDownloadStarted(int count) => AppLogger.Log($"[NOTIFY] {count} download(s) started.");
}

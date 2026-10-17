using YTDLPHost.Models;

namespace YTDLPHost.Services
{
    /// <summary>
    /// What <see cref="DownloadQueue"/> needs from something that runs one download. <see cref="YtDlpRunner"/>
    /// is the real implementation; tests use a fake so scheduling can be tested without yt-dlp.
    /// Events may be raised on any thread (for the real runner: the process I/O threads).
    /// </summary>
    public interface IDownloadRunner : IDisposable
    {
        event EventHandler<ProgressEventArgs>? OnProgressUpdate;
        event EventHandler<CompleteEventArgs>? OnDownloadComplete;
        event EventHandler<DownloadErrorEventArgs>? OnDownloadError;
        event EventHandler<ExtractedInfoEventArgs>? OnInfoExtracted;

        /// <summary>Runs the download to its end (or until <see cref="Cancel"/>), reporting through the task and the events.</summary>
        Task ExecuteAsync(DownloadTask task);

        /// <summary>Stops the download. After a cancel the runner must not change the task's status (the caller owns it).</summary>
        void Cancel();
    }
}

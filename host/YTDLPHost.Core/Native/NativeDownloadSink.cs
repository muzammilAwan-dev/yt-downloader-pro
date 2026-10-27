using System.Collections.ObjectModel;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Native
{
    /// <summary>
    /// Feeds browser requests into the same intake and queue the ytdlp:// path uses (<see cref="DownloadIntake.SubmitNative"/>).
    /// The whole submit runs inside ONE dispatcher call, on the UI thread: <see cref="DownloadIntake"/> holds a lock while it waits for
    /// that thread, so letting a pipe thread take the lock first could deadlock against a ytdlp:// link handled on the UI thread.
    /// </summary>
    public sealed class NativeDownloadSink : INativeDownloadSink
    {
        private readonly DownloadIntake _intake;
        private readonly ObservableCollection<DownloadItemViewModel> _downloads;
        private readonly IUiDispatcher _ui;
        private readonly Action<DownloadItemViewModel>? _onAdded;
        private readonly Action? _afterAdd;
        private readonly Func<string?> _unavailable;

        /// <param name="onAdded">Runs on the UI thread for each NEW item, in the same dispatcher call that adds it (status text and so on).</param>
        /// <param name="afterAdd">Runs on the UI thread once after the request was queued (count refresh and starting the queue).</param>
        /// <param name="unavailable">Returns a reason when the app cannot take downloads right now (for example the first-start setup failed), otherwise null.</param>
        public NativeDownloadSink(DownloadIntake intake, ObservableCollection<DownloadItemViewModel> downloads, IUiDispatcher ui,
            Action<DownloadItemViewModel>? onAdded, Action? afterAdd, Func<string?> unavailable)
        {
            _intake = intake;
            _downloads = downloads;
            _ui = ui;
            _onAdded = onAdded;
            _afterAdd = afterAdd;
            _unavailable = unavailable;
        }

        public NativeSubmission Submit(IReadOnlyList<MappedDownload> downloads)
        {
            string? reason = _unavailable();
            if (reason != null) return new NativeSubmission(Array.Empty<DownloadItemViewModel>(), reason);

            var items = new List<DownloadItemViewModel>();
            string? error = null;

            _ui.Invoke(() =>
            {
                try
                {
                    foreach (var download in downloads)
                    {
                        var result = _intake.SubmitNative(download.Command, download.CookiePayload, download.UserAgent, _onAdded);

                        if (result.Item != null && (result.Status == IntakeStatus.Added || result.Status == IntakeStatus.Duplicate))
                        {
                            if (!items.Contains(result.Item)) items.Add(result.Item);
                            continue;
                        }

                        error = result.Status == IntakeStatus.Blocked
                            ? $"The download was refused by the command checker: {result.BlockReason}"
                            : "The download could not be added.";
                        break;
                    }

                    if (items.Count > 0) _afterAdd?.Invoke();
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[NATIVE] Could not queue a browser request: {ex.Message}");
                    error = "The app could not add the download.";
                }
            });

            return new NativeSubmission(items, error);
        }

        public HashSet<Guid> ListedIds()
        {
            var ids = new HashSet<Guid>();
            _ui.Invoke(() =>
            {
                foreach (var item in _downloads) ids.Add(item.Id);
            });
            return ids;
        }
    }
}

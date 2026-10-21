using System.Collections.ObjectModel;
using YTDLPHost.Models;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Services
{
    public enum IntakeStatus { Ignored, Blocked, Duplicate, Added }

    /// <param name="Item">The new list item when <see cref="Status"/> is Added, otherwise null.</param>
    /// <param name="BlockReason">The validator's reason when <see cref="Status"/> is Blocked, otherwise null.</param>
    public sealed record IntakeResult(IntakeStatus Status, DownloadItemViewModel? Item = null, string? BlockReason = null);

    /// <summary>
    /// Turns one raw ytdlp:// link into a queued list item: parse and validate (<see cref="CommandIngest"/>), drop an exact
    /// duplicate, write the cookie file, build the task and add it to the list. Extracted from MainViewModel.ProcessUrl
    /// (step 1.5f). Behavior kept as it was:
    ///  * the duplicate check runs BEFORE the cookie file is written (a repeated link must not leave a cookie file behind);
    ///  * only an item that is Queued or Downloading with the same command counts as a duplicate;
    ///  * check and add happen under one lock, so two identical links arriving together add one item;
    ///  * malformed base64 still throws FormatException out of <see cref="Submit"/> (the caller logs and drops it).
    /// The item list belongs to the UI thread, so it is only read or changed inside <see cref="IUiDispatcher"/> calls.
    /// </summary>
    public sealed class DownloadIntake
    {
        private readonly CommandIngest _ingest;
        private readonly ObservableCollection<DownloadItemViewModel> _downloads;
        private readonly IUiDispatcher _ui;
        private readonly IClipboardService? _clipboard;
        private readonly Action<DownloadItemViewModel>? _attach;
        private readonly object _gate = new();

        /// <param name="attach">Runs on the calling thread for each new item BEFORE it enters the list (the view model hooks its history observer here).</param>
        public DownloadIntake(CommandIngest ingest, ObservableCollection<DownloadItemViewModel> downloads, IUiDispatcher ui,
            IClipboardService? clipboard = null, Action<DownloadItemViewModel>? attach = null)
        {
            _ingest = ingest;
            _downloads = downloads;
            _ui = ui;
            _clipboard = clipboard;
            _attach = attach;
        }

        /// <param name="onAdded">Runs on the UI thread, in the same dispatcher call that adds the item to the list.</param>
        public IntakeResult Submit(string? rawUrl, Action<DownloadItemViewModel>? onAdded = null)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return new IntakeResult(IntakeStatus.Ignored);

            var parsed = _ingest.Parse(rawUrl);

            if (parsed.Status == IngestStatus.Ignored) return new IntakeResult(IntakeStatus.Ignored);
            if (parsed.Status == IngestStatus.Blocked) return new IntakeResult(IntakeStatus.Blocked, BlockReason: parsed.BlockReason);

            var payload = parsed.Payload!;
            string command = payload.Command;

            lock (_gate)
            {
                bool isDuplicate = false;
                _ui.Invoke(() =>
                {
                    isDuplicate = _downloads.Any(d =>
                        d.Task.Command == command &&
                        (d.Task.Status == DownloadStatus.Queued || d.Task.Status == DownloadStatus.Downloading));
                });

                if (isDuplicate)
                {
                    AppLogger.Log("[QUEUE] Exact duplicate command detected in queue. Ignoring ghost echo.");
                    return new IntakeResult(IntakeStatus.Duplicate);
                }

                var prepared = _ingest.Prepare(payload);

                var task = new DownloadTask
                {
                    UrlPayload = payload.UrlPayload,
                    Command = prepared.Command,
                    CookiePayload = prepared.CookieContent ?? "",
                    CookieFilePath = prepared.CookieFilePath,
                    Resolution = CommandInfo.ExtractResolution(prepared.Command),
                    Title = CommandInfo.ExtractTitleHint(prepared.Command),
                    Status = DownloadStatus.Queued
                };

                var vm = new DownloadItemViewModel(task, _clipboard);
                _attach?.Invoke(vm);

                _ui.Invoke(() =>
                {
                    _downloads.Add(vm);
                    onAdded?.Invoke(vm);
                });

                return new IntakeResult(IntakeStatus.Added, vm);
            }
        }
    }
}

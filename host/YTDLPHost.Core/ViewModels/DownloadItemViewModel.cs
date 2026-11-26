using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTDLPHost.Models;
using YTDLPHost.Services;

namespace YTDLPHost.ViewModels
{
    public partial class DownloadItemViewModel : ObservableObject
    {
        private readonly IClipboardService? _clipboard;
        private readonly IUiDispatcher? _ui;

        private const string GenericErrorHint = "Try again. If it keeps failing, use Update download engine in Settings and tools.";

        [ObservableProperty]
        private DownloadTask _task;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isLogVisible;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ErrorDetailsToggleText))]
        private bool _isErrorDetailsVisible;

        public Guid Id => Task.Id;

        /// <summary>
        /// Gets the display title for the UI. Displays a clean placeholder while the actual title is being extracted.
        /// </summary>
        // yt-dlp saves files with underscores and a resolution suffix ("My_Video_Title_1080p"). The card shows the title the way a person
        // would read it, because the quality already has its own chip.
        private static readonly Regex ResolutionSuffix = new(@"[\s_]+\d{3,4}p$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ExtraSpaces = new(@"\s{2,}", RegexOptions.Compiled);

        private static string CleanTitle(string raw)
        {
            string text = raw.Replace('_', ' ');
            text = ResolutionSuffix.Replace(text, "");
            text = ExtraSpaces.Replace(text, " ").Trim();
            return text.Length == 0 ? raw : text;
        }

        public string DisplayTitle
        {
            get
            {
                bool unknown = string.IsNullOrWhiteSpace(Task.Title) || Task.Title == "Unknown" || Task.Title.StartsWith("Fetching", StringComparison.OrdinalIgnoreCase);
                if (!unknown) return CleanTitle(Task.Title);

                // "Fetching video..." only makes sense while it is actually being fetched.
                return Task.Status == DownloadStatus.Downloading && !IsProcessing
                    ? "Fetching video..."
                    : "Untitled download";
            }
        }

        /// <summary>Safe display text only. The handoff payload may contain cookies and is never UI content.</summary>
        public string TitleTooltip => DisplayTitle;

        /// <summary>
        /// True while yt-dlp/ffmpeg are working on the file (merging, converting, embedding) rather than moving bytes.
        /// Such a download has no meaningful speed, so the UI shows a "processing" state instead of "0 B/s".
        /// </summary>
        public bool IsProcessing
        {
            get
            {
                if (Task.Status != DownloadStatus.Downloading) return false;
                string phase = Task.CurrentPhase ?? "";
                return phase.StartsWith("Merging", StringComparison.OrdinalIgnoreCase) ||
                       phase.StartsWith("Embedding", StringComparison.OrdinalIgnoreCase) ||
                       phase.StartsWith("Converting", StringComparison.OrdinalIgnoreCase) ||
                       phase.StartsWith("Adding", StringComparison.OrdinalIgnoreCase) ||
                       phase.StartsWith("Removing", StringComparison.OrdinalIgnoreCase);
            }
        }

        public string ResolutionDisplay => Task.Resolution;
        public double Progress => Task.Progress;
        public string ErrorMessage => Task.ErrorMessage;

        // ---------- Card text, split into the pieces the new layout shows separately ----------

        /// <summary>Short status name, e.g. "Failed". Also the change signal the window uses to refresh filters and counts.</summary>
        public string StatusLabel => Task.Status switch
        {
            DownloadStatus.Queued => "In queue",
            DownloadStatus.Downloading => "Downloading",
            DownloadStatus.Paused => "Paused",
            DownloadStatus.Completed => "Completed",
            DownloadStatus.Error => "Failed",
            DownloadStatus.Cancelled => "Cancelled",
            _ => "Unknown"
        };

        /// <summary>
        /// Which state glyph the card shows: queued, downloading, processing (merging or converting), paused, completed, failed or cancelled.
        /// The glyph's shape and colour both follow it, so state is never told by colour alone.
        /// </summary>
        public string StatusKind => Task.Status switch
        {
            DownloadStatus.Downloading => IsProcessing ? "processing" : "downloading",
            DownloadStatus.Paused => "paused",
            DownloadStatus.Completed => "completed",
            DownloadStatus.Error => "failed",
            DownloadStatus.Cancelled => "cancelled",
            _ => "queued"
        };

        /// <summary>The one line under the title: what is happening right now, in plain words.</summary>
        public string StatusLine
        {
            get
            {
                switch (Task.Status)
                {
                    case DownloadStatus.Completed:
                    {
                        string done = string.IsNullOrEmpty(Task.ResultNote) ? "Completed" : $"Completed \u00B7 {Task.ResultNote}";
                        if (Task.CompletedAt is DateTime finished) done += $" \u00B7 {finished:t}";
                        return done;
                    }

                    case DownloadStatus.Error:
                        return ErrorTitle;

                    case DownloadStatus.Cancelled:
                        return "Cancelled";

                    case DownloadStatus.Paused:
                        return Task.Progress > 0 ? $"Paused at {Task.Progress:0.0}%" : "Paused";

                    case DownloadStatus.Queued:
                        return Task.CurrentPhase.Contains("Similar video", StringComparison.OrdinalIgnoreCase)
                            ? "Waiting for the same video to finish"
                            : "In queue";
                }

                string phase = Task.CurrentPhase.Trim().TrimEnd('.', '\u2026');
                string line = string.IsNullOrEmpty(Task.PlaylistInfo) ? phase : $"{Task.PlaylistInfo} \u00B7 {phase}";

                if (IsProcessing) return line;

                if (Task.IsIndeterminate)
                {
                    // Live or unknown-length downloads have no percentage, but they do have a size so far.
                    if (Task.CurrentPhase.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(Task.FileSize))
                        line += $" \u00B7 {Task.FileSize}";
                    return line;
                }

                if (Task.Progress > 0)
                {
                    line += $" \u00B7 {Task.Progress:0.0}%";
                    if (!string.IsNullOrEmpty(Task.FileSize)) line += $" of {Task.FileSize}";
                }
                return line;
            }
        }

        /// <summary>Current speed as yt-dlp reports it, e.g. "3.30MiB/s".</summary>
        public string SpeedText => IsTransferMetricsVisible ? Task.Speed ?? "" : "";

        // The card shows speed as a big number with a small unit ("3.30" + "MiB/s"), like an instrument readout.
        private static readonly Regex SpeedParts = new(@"^\s*(?<n>[0-9]*\.?[0-9]+)\s*(?<u>\S.*)?$", RegexOptions.Compiled);

        public string SpeedValue
        {
            get
            {
                string speed = SpeedText;
                var m = SpeedParts.Match(speed);
                return m.Success ? m.Groups["n"].Value : speed;
            }
        }

        public string SpeedUnit
        {
            get
            {
                var m = SpeedParts.Match(SpeedText);
                return m.Success ? m.Groups["u"].Value.Trim() : "";
            }
        }

        /// <summary>"ETA 00:07", or "00:42 elapsed" for live streams where the end is unknown.</summary>
        public string EtaText
        {
            get
            {
                if (!IsTransferMetricsVisible || string.IsNullOrEmpty(Task.Eta) || Task.Eta == "Unknown") return "";
                return Task.IsIndeterminate ? $"{Task.Eta} elapsed" : $"ETA {Task.Eta}";
            }
        }

        // ---------- Errors: plain-language title + what to do, raw yt-dlp text kept behind "Show details" ----------

        public string ErrorTitle => DescribeError(Task.ErrorMessage).Title;
        public string ErrorHint => DescribeError(Task.ErrorMessage).Hint;
        public string ErrorDetailsToggleText => IsErrorDetailsVisible ? "Hide details" : "Show details";

        /// <summary>
        /// Kept for compatibility with older bindings. The new card shows StatusLine, SpeedText and EtaText separately.
        /// </summary>
        public string ProgressDisplay
        {
            get
            {
                if (Task.Status == DownloadStatus.Completed)
                {
                    // Bulk jobs show what happened, e.g. "Completed - 1 saved, 120 failed (DRM-protected)".
                    return string.IsNullOrEmpty(Task.ResultNote) ? "Completed" : $"Completed - {Task.ResultNote}";
                }
                if (Task.Status == DownloadStatus.Error) return "Error";
                if (Task.Status == DownloadStatus.Cancelled) return "Cancelled";
                if (Task.Status == DownloadStatus.Paused) return "Paused";

                // THE UX FIX: Allow the Mutex "Waiting" message to actually reach the UI instead of being overwritten by "Queued"
                if (Task.Status == DownloadStatus.Queued)
                {
                    return Task.CurrentPhase.Contains("Waiting") ? Task.CurrentPhase : "Queued";
                }

                string prefix = string.IsNullOrEmpty(Task.PlaylistInfo)
                    ? Task.CurrentPhase
                    : $"{Task.PlaylistInfo} - {Task.CurrentPhase}";

                if (Task.IsIndeterminate)
                {
                    // Live / unknown-length downloads have no percentage, but they do have size, speed and elapsed time.
                    if (Task.CurrentPhase.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(Task.Eta))
                    {
                        string live = prefix;
                        if (!string.IsNullOrEmpty(Task.FileSize)) live += $" | {Task.FileSize}";
                        if (!string.IsNullOrEmpty(Task.Speed)) live += $" | {Task.Speed}";
                        return $"{live} | {Task.Eta} elapsed";
                    }
                    return prefix;
                }

                string sizeStr = string.IsNullOrEmpty(Task.FileSize) ? "" : $" of {Task.FileSize}";
                string details = $" | {Task.Progress:0.0}%{sizeStr}";

                if (!string.IsNullOrEmpty(Task.Speed)) details += $" | {Task.Speed}";
                if (!string.IsNullOrEmpty(Task.Eta) && Task.Eta != "Unknown") details += $" | ETA: {Task.Eta}";

                return $"{prefix}{details}";
            }
        }

        public bool IsCompleted => Task.Status == DownloadStatus.Completed;
        public bool IsActive => Task.Status == DownloadStatus.Downloading || Task.Status == DownloadStatus.Queued;
        public bool IsDownloading => Task.Status == DownloadStatus.Downloading;
        public bool IsTransferMetricsVisible => IsDownloading && !IsProcessing;
        public bool IsProgressIndeterminate => IsDownloading && (IsProcessing || Task.IsIndeterminate);
        public string ProgressAutomationName => $"{DisplayTitle}: {StatusLine}";
        public bool IsPaused => Task.Status == DownloadStatus.Paused;
        public bool HasError => Task.Status == DownloadStatus.Error;
        /// <summary>The bar only appears while bytes are (or were) moving. Queued, finished and failed cards stay calm.</summary>
        public bool IsProgressVisible => Task.Status == DownloadStatus.Downloading || Task.Status == DownloadStatus.Paused;
        public bool IsResumable => Task.Status == DownloadStatus.Paused || Task.Status == DownloadStatus.Error;
        public bool CanBePaused => IsActive;
        public bool CanBeCancelled => IsActive || Task.Status == DownloadStatus.Paused;
        public bool CanBeRemoved => Task.Status == DownloadStatus.Completed || Task.Status == DownloadStatus.Error || Task.Status == DownloadStatus.Cancelled;

        public ICommand ToggleLogCommand { get; }
        public ICommand ToggleErrorDetailsCommand { get; }

        public DownloadItemViewModel(DownloadTask task, IClipboardService? clipboard = null, IUiDispatcher? ui = null)
        {
            _clipboard = clipboard;
            _ui = ui;
            Task = task;

            Task.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Task.Status))
                {
                    RefreshStateProperties();
                }
                else if (e.PropertyName == nameof(Task.Speed) ||
                         e.PropertyName == nameof(Task.Eta) ||
                         e.PropertyName == nameof(Task.FileSize) ||
                         e.PropertyName == nameof(Task.CurrentPhase) ||
                         e.PropertyName == nameof(Task.PlaylistInfo) ||
                         e.PropertyName == nameof(Task.ResultNote) ||
                         e.PropertyName == nameof(Task.IsIndeterminate) ||
                         e.PropertyName == nameof(Task.Progress))
                {
                    // yt-dlp reports progress many times per second, from a background thread.
                    // Batch those into one UI refresh every ~150 ms so the window stays responsive.
                    ScheduleLiveRefresh();
                }
                else if (e.PropertyName == nameof(Task.ErrorMessage))
                {
                    OnPropertyChanged(nameof(ErrorMessage));
                    OnPropertyChanged(nameof(ErrorTitle));
                    OnPropertyChanged(nameof(ErrorHint));
                    OnPropertyChanged(nameof(StatusLine));
                }
                else if (e.PropertyName == nameof(Task.Title))
                {
                    OnPropertyChanged(nameof(DisplayTitle));
                    OnPropertyChanged(nameof(TitleTooltip));
                    OnPropertyChanged(nameof(ProgressAutomationName));
                }
                else if (e.PropertyName == nameof(Task.Resolution))
                {
                    OnPropertyChanged(nameof(ResolutionDisplay));
                }
            };

            ToggleLogCommand = new RelayCommand(() => IsLogVisible = !IsLogVisible);
            ToggleErrorDetailsCommand = new RelayCommand(() => IsErrorDetailsVisible = !IsErrorDetailsVisible);
            CopyLogCommand = new RelayCommand(() => CopyToClipboard(Task.FullLogText));
            CopyErrorCommand = new RelayCommand(() => CopyToClipboard(Task.ErrorMessage));
        }

        public ICommand CopyLogCommand { get; }
        public ICommand CopyErrorCommand { get; }

        // The live log text is only built while the log panel is open (see DownloadTask.SetUiLogActive).
        partial void OnIsLogVisibleChanged(bool value) => Task.SetUiLogActive(value);

        private int _liveRefreshPending;

        private void ScheduleLiveRefresh()
        {
            if (Interlocked.Exchange(ref _liveRefreshPending, 1) == 1) return;

            _ = System.Threading.Tasks.Task.Delay(150).ContinueWith(_ =>
            {
                // Property-changed notifications reach the screen through the UI thread; without a dispatcher (tests) run in place.
                if (_ui != null) _ui.Post(FlushLiveRefresh);
                else FlushLiveRefresh();
            });
        }

        private void FlushLiveRefresh()
        {
            Interlocked.Exchange(ref _liveRefreshPending, 0);
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(IsProcessing));
            OnPropertyChanged(nameof(StatusKind));
            OnPropertyChanged(nameof(IsTransferMetricsVisible));
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(ProgressAutomationName));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(TitleTooltip));
            OnPropertyChanged(nameof(StatusLine));
            OnPropertyChanged(nameof(SpeedText));
            OnPropertyChanged(nameof(SpeedValue));
            OnPropertyChanged(nameof(SpeedUnit));
            OnPropertyChanged(nameof(EtaText));
            OnPropertyChanged(nameof(ProgressDisplay));
        }

        // The on-screen log keeps only the last 100 lines; the copy button gives the complete log. The clipboard can be
        // locked by another app for a moment (COMException), which must never crash the UI.
        private void CopyToClipboard(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { _clipboard?.SetText(text); } catch { }
        }

        public void Refresh()
        {
            RefreshStateProperties();
        }

        private void RefreshStateProperties()
        {
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(TitleTooltip));
            OnPropertyChanged(nameof(IsProcessing));
            OnPropertyChanged(nameof(IsTransferMetricsVisible));
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(ProgressAutomationName));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsDownloading));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsProgressVisible));
            OnPropertyChanged(nameof(IsResumable));
            OnPropertyChanged(nameof(CanBePaused));
            OnPropertyChanged(nameof(CanBeCancelled));
            OnPropertyChanged(nameof(CanBeRemoved));
            OnPropertyChanged(nameof(ProgressDisplay));
            OnPropertyChanged(nameof(StatusLine));
            OnPropertyChanged(nameof(SpeedText));
            OnPropertyChanged(nameof(SpeedValue));
            OnPropertyChanged(nameof(SpeedUnit));
            OnPropertyChanged(nameof(EtaText));
            OnPropertyChanged(nameof(ErrorTitle));
            OnPropertyChanged(nameof(ErrorHint));
            OnPropertyChanged(nameof(StatusKind));
            OnPropertyChanged(nameof(StatusLabel)); // last: the window listens to this one to refresh filters and counts
        }

        /// <summary>
        /// Turns raw yt-dlp output into a short title plus a next step (Nielsen Norman Group: human-readable, precise, constructive).
        /// Messages the app already wrote in plain language are split into first sentence + the rest.
        /// The untouched original is always available under "Show details".
        /// </summary>
        private static (string Title, string Hint) DescribeError(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return ("Download failed", GenericErrorHint);

            string text = raw.Trim();
            string lower = text.ToLowerInvariant();

            bool looksRaw = text.Contains("ERROR:") || text.Contains("WARNING:") || text.Contains('\n') ||
                            text.StartsWith("Execution error", StringComparison.OrdinalIgnoreCase) ||
                            text.StartsWith("Download failed (Code", StringComparison.OrdinalIgnoreCase);

            if (!looksRaw)
            {
                int cut = text.IndexOf(". ", StringComparison.Ordinal);
                if (cut > 0 && cut < text.Length - 2) return (text.Substring(0, cut + 1), text.Substring(cut + 2).Trim());
                return (text, "");
            }

            if (lower.Contains("subtitle") && (lower.Contains("429") || lower.Contains("too many requests")))
                return ("The site blocked the subtitles download (HTTP 429)",
                        "The video itself is fine. Retry in a few minutes, or turn subtitles off in the extension.");

            if (lower.Contains("429") || lower.Contains("too many requests"))
                return ("The site is limiting requests (HTTP 429)",
                        "Wait 15\u201360 minutes before retrying. Lowering Downloads at once in Limits also helps.");

            if (lower.Contains("sign in to confirm") || lower.Contains("not a bot"))
                return ("The site wants to confirm you\u2019re not a bot",
                        "Sign in to the site in your browser, turn on Bypass Age & Bot issue in the extension, then retry.");

            if (lower.Contains("private video") || lower.Contains("video unavailable") || lower.Contains("this video is not available") ||
                lower.Contains("has been removed") || lower.Contains("members-only") || lower.Contains("members only"))
                return ("This video isn\u2019t available",
                        "It may be private, removed, members-only or blocked in your region.");

            if (lower.Contains("requested format is not available"))
                return ("That quality isn\u2019t offered for this video",
                        "Retry with a lower quality from the extension.");

            if (lower.Contains("http error 403") || lower.Contains("403: forbidden"))
                return ("The site refused the download (HTTP 403)",
                        "Update the download engine in Settings and tools, then retry.");

            if (lower.Contains("ffmpeg") && (lower.Contains("not found") || lower.Contains("not installed") || lower.Contains("postprocessing")))
                return ("Joining video and audio failed",
                        "Update the download engine in Settings and tools so ffmpeg is checked again, then retry.");

            if (lower.Contains("unable to connect") || lower.Contains("getaddrinfo") || lower.Contains("timed out") ||
                lower.Contains("network is unreachable") || lower.Contains("connection reset") || lower.Contains("temporary failure in name resolution"))
                return ("Connection problem", "Check your internet connection, then retry.");

            if (lower.Contains("no space left") || lower.Contains("not enough space") || lower.Contains("disk full"))
                return ("Not enough disk space", "Free some space or save to another folder, then retry.");

            if (lower.Contains("permission denied") || lower.Contains("access is denied"))
                return ("Can\u2019t write to the save folder", "Pick a different folder in the extension, then retry.");

            // Unknown: show the first meaningful line, without the "ERROR:" prefix.
            string line = text;
            foreach (var candidate in text.Split('\n'))
            {
                if (candidate.Contains("ERROR:")) { line = candidate; break; }
            }
            if (line == text) line = text.Split('\n')[0];
            line = line.Trim();
            int marker = line.IndexOf("ERROR:", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0) line = line.Substring(marker + 6).Trim();
            if (line.Length > 140) line = line.Substring(0, 137) + "\u2026";
            return (string.IsNullOrEmpty(line) ? "Download failed" : line, GenericErrorHint);
        }
    }
}

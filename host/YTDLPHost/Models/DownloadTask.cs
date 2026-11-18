using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace YTDLPHost.Models
{
    public enum DownloadStatus
    {
        Queued,
        Downloading,
        Paused,
        Completed,
        Error,
        Cancelled
    }

    public partial class DownloadTask : ObservableObject
    {
        [ObservableProperty]
        private Guid _id = Guid.NewGuid();

        [ObservableProperty]
        private string _title = "Unknown";

        [ObservableProperty]
        private string _resolution = "";

        // SECURITY FIX: UrlPayload embeds the raw base64 command||cookies string exactly
        // as received from the extension. It must never be written to history.json.
        [property: JsonIgnore]
        [ObservableProperty]
        private string _urlPayload = "";

        [ObservableProperty]
        private string _command = "";

        // SECURITY FIX: CookiePayload is the decoded Netscape-format session cookie file
        // (YouTube auth cookies). HistoryManager persists tasks to disk for auto-recovery,
        // so leaving this serializable would write live session cookies to
        // %LOCALAPPDATA%\YTDownloaderProEngine\history.json in plaintext, indefinitely.
        [property: JsonIgnore]
        [ObservableProperty]
        private string _cookiePayload = "";

        [ObservableProperty]
        private string _cookieFilePath = "";

        [ObservableProperty]
        private double _progress = 0.0;

        [ObservableProperty]
        private string _speed = "";

        [ObservableProperty]
        private string _eta = "";

        [ObservableProperty]
        private string _fileSize = "";

        [ObservableProperty]
        private DownloadStatus _status = DownloadStatus.Queued;

        [ObservableProperty]
        private string _errorMessage = "";

        [ObservableProperty]
        private string _outputPath = "";

        [ObservableProperty]
        private string _fileName = "";

        [ObservableProperty]
        private string _currentPhase = "Starting...";

        [ObservableProperty]
        private bool _isIndeterminate = false;

        [ObservableProperty]
        private string _playlistInfo = "";

        [ObservableProperty]
        private DateTime _queuedAt = DateTime.Now;

        [ObservableProperty]
        private DateTime? _startedAt;

        [ObservableProperty]
        private DateTime? _completedAt;

        [ObservableProperty]
        private bool _logFileSaved;

        // One-line summary for bulk jobs once they finish, e.g. "1 saved, 120 skipped (120 DRM-protected)". Shown next to "Completed".
        [ObservableProperty]
        private string _resultNote = "";

        // --- IGNORED FOR JSON SERIALIZATION --- 
        // We only save core metadata to history.json. We discard volatile UI logs to prevent massive memory leaks.

        // Files of the item that is downloading RIGHT NOW. Only these are fair game for cleanup when the task is cancelled.
        [JsonIgnore]
        public ConcurrentBag<string> TrackedFiles { get; } = new();

        // Files of items that already finished (or were already on disk before this task started). Cancelling or finishing a bulk
        // job must NEVER delete these - they are the user's downloads.
        [JsonIgnore]
        public ConcurrentBag<string> SettledFiles { get; } = new();

        /// <summary>Called when a bulk job moves on to its next item: everything tracked so far belongs to a finished item now.</summary>
        public void SettleCurrentItem()
        {
            foreach (var f in TrackedFiles.Distinct().ToList()) SettledFiles.Add(f);
            while (!TrackedFiles.IsEmpty)
            {
                TrackedFiles.TryTake(out _);
            }
        }

        [JsonIgnore]
        private readonly StringBuilder _fullLogBuilder = new();

        [JsonIgnore]
        private readonly List<string> _uiLogLines = new();

        [JsonIgnore]
        private const int MaxUiLogLines = 3000;

        [JsonIgnore]
        private readonly object _logLock = new();

        // Progress lines replace each other (like a terminal's carriage return) instead of piling up by the thousand.
        private bool _lastLineWasProgress;
        private int _fullLogLastLineStart = -1;

        // The on-screen log is only built while someone is looking at it, and at most 4 times a second.
        // yt-dlp prints a progress line many times per second; rebuilding and re-laying-out the log text for each one
        // froze the whole window (clicks and scrolling were delayed or lost while a download ran).
        private volatile bool _uiLogActive;
        private int _uiLogFlushPending;

        [JsonIgnore]
        public string FullLogText
        {
            get { lock (_logLock) { return _fullLogBuilder.ToString(); } }
        }

        [property: JsonIgnore]
        [ObservableProperty]
        private string _uiLogText = "";

        private static bool IsProgressLine(string line) =>
            line.StartsWith("[download]", StringComparison.Ordinal) && line.Contains('%') &&
            (line.Contains(" ETA ", StringComparison.Ordinal) || line.Contains(" at ", StringComparison.Ordinal));

        /// <summary>Tells the task whether its log panel is open. Opening it shows the current log immediately.</summary>
        public void SetUiLogActive(bool active)
        {
            _uiLogActive = active;
            if (active) FlushUiLog();
        }

        private void ScheduleUiLogFlush()
        {
            if (!_uiLogActive) return;
            if (Interlocked.Exchange(ref _uiLogFlushPending, 1) == 1) return;
            _ = System.Threading.Tasks.Task.Delay(250).ContinueWith(_ => FlushUiLog());
        }

        private void FlushUiLog()
        {
            Interlocked.Exchange(ref _uiLogFlushPending, 0);
            string text;
            lock (_logLock)
            {
                text = string.Join(Environment.NewLine, _uiLogLines);
            }
            UiLogText = text;
        }

        public void AppendLog(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_logLock)
            {
                bool isProgress = IsProgressLine(line);
                if (isProgress && _lastLineWasProgress)
                {
                    // Replace the previous progress line, in both the full log and the on-screen log.
                    if (_fullLogLastLineStart >= 0 && _fullLogLastLineStart <= _fullLogBuilder.Length)
                        _fullLogBuilder.Length = _fullLogLastLineStart;
                    if (_uiLogLines.Count > 0) _uiLogLines.RemoveAt(_uiLogLines.Count - 1);
                }

                _fullLogLastLineStart = _fullLogBuilder.Length;
                _fullLogBuilder.AppendLine(line);

                _uiLogLines.Add(line);
                if (_uiLogLines.Count > MaxUiLogLines) _uiLogLines.RemoveAt(0);

                _lastLineWasProgress = isProgress;
            }
            ScheduleUiLogFlush();
        }

        /// <summary>Writes a line to the full on-disk log only (not the live log view). Used for noisy ffmpeg diagnostics.</summary>
        public void AppendLogQuiet(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_logLock)
            {
                _fullLogBuilder.AppendLine(line);
                _lastLineWasProgress = false; // a quiet line sits in the full log now, so the next progress line must not overwrite it
            }
        }

        public void ClearLog()
        {
            lock (_logLock)
            {
                _fullLogBuilder.Clear();
                _uiLogLines.Clear();
                _lastLineWasProgress = false;
                _fullLogLastLineStart = -1;
                UiLogText = "";
                LogFileSaved = false;
                
                while (!TrackedFiles.IsEmpty)
                {
                    TrackedFiles.TryTake(out _);
                }
                while (!SettledFiles.IsEmpty)
                {
                    SettledFiles.TryTake(out _);
                }
                ResultNote = "";
            }
        }
    }
}
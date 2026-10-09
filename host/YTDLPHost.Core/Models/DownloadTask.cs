using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;
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

        // --- IGNORED FOR JSON SERIALIZATION --- 
        // We only save core metadata to history.json. We discard volatile UI logs to prevent massive memory leaks.

        [JsonIgnore]
        public ConcurrentBag<string> TrackedFiles { get; } = new();

        [JsonIgnore]
        private readonly StringBuilder _fullLogBuilder = new();
        
        [JsonIgnore]
        private readonly Queue<string> _uiLogQueue = new();
        
        [JsonIgnore]
        private const int MaxUiLogLines = 100; 
        
        [JsonIgnore]
        private readonly object _logLock = new();
        
        [JsonIgnore]
        public string FullLogText => _fullLogBuilder.ToString();

        [property: JsonIgnore]
        [ObservableProperty]
        private string _uiLogText = "";

        public void AppendLog(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_logLock)
            {
                _fullLogBuilder.AppendLine(line);
                
                _uiLogQueue.Enqueue(line);
                if (_uiLogQueue.Count > MaxUiLogLines)
                {
                    _uiLogQueue.Dequeue();
                }

                UiLogText = string.Join(Environment.NewLine, _uiLogQueue);
            }
        }

        public void ClearLog()
        {
            lock (_logLock)
            {
                _fullLogBuilder.Clear();
                _uiLogQueue.Clear();
                UiLogText = "";
                LogFileSaved = false;
                
                while (!TrackedFiles.IsEmpty)
                {
                    TrackedFiles.TryTake(out _);
                }
            }
        }
    }
}
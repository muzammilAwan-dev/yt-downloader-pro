using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTDLPHost.Models;
using YTDLPHost.Services;

namespace YTDLPHost.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private static readonly HttpClient _httpClient = CreateConfiguredHttpClient();

        private readonly ObservableCollection<DownloadItemViewModel> _downloads = new();
        private readonly object _duplicateLock = new(); 
        
        public AppSettings Settings { get; }

        private bool _disposed;

        [ObservableProperty] private bool _isWindowVisible = true;
        [ObservableProperty] private string _statusText = "Ready";
        [ObservableProperty] private int _activeDownloadCount = 0;
        [ObservableProperty] private bool _hasDownloads;
        [ObservableProperty] private bool _hasCompletedDownloads;
        [ObservableProperty] private string _emptyStateText = "No active downloads. Click Download on any supported site to add videos.";
        [ObservableProperty] private DownloadItemViewModel? _selectedItem;

        public ObservableCollection<DownloadItemViewModel> Downloads => _downloads;

        public IRelayCommand<string> ProcessUrlCommand { get; }
        public IRelayCommand<DownloadItemViewModel> PauseDownloadCommand { get; }
        public IRelayCommand<DownloadItemViewModel> CancelDownloadCommand { get; }
        public IRelayCommand<DownloadItemViewModel> ResumeDownloadCommand { get; }
        public IRelayCommand<DownloadItemViewModel> RemoveDownloadCommand { get; }
        public IRelayCommand<DownloadItemViewModel> OpenFolderCommand { get; }
        public IRelayCommand<DownloadItemViewModel> PlayFileCommand { get; }
        public IRelayCommand ClearCompletedCommand { get; }
        public IRelayCommand ShowWindowCommand { get; }
        public IRelayCommand ExitCommand { get; }
        public IRelayCommand MinimizeToTrayCommand { get; }
        public IRelayCommand SaveSettingsCommand { get; }

        public event EventHandler? RequestShowWindow;
        public event EventHandler<DownloadItemViewModel>? RequestScrollToItem;

        private static HttpClient CreateConfiguredHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            client.DefaultRequestHeaders.Add("User-Agent", "YTDownloaderPro/6.0 (Windows NT 10.0; Win64; x64)");
            return client;
        }

        private readonly IUiDispatcher _ui;
        private readonly IDialogService _dialogs;
        private readonly IAppLifetime _lifetime;
        private readonly IClipboardService _clipboard;
        private readonly ITrayService _tray;
        private readonly INotifier _notifier;
        private readonly DownloadOpener _opener;
        private readonly HistorySaver _history;
        private readonly EngineSetup _setup;
        private readonly EngineProvisioner _engine;
        private readonly CommandIngest _ingest;
        private readonly DownloadQueue _queue;

        public MainViewModel(PlatformServices platform)
        {
            _ui = platform.Ui;
            _dialogs = platform.Dialogs;
            _lifetime = platform.Lifetime;
            _clipboard = platform.Clipboard;
            _tray = platform.Tray;
            _notifier = platform.Notifier;
            _opener = new DownloadOpener(platform.Shell);
            _engine = new EngineProvisioner(AppPaths.Current, _httpClient);
            _ingest = new CommandIngest(AppPaths.Current);

            // Scheduling lives in DownloadQueue (Core). The limit is read from the saved settings each time, as before.
            _queue = new DownloadQueue(_downloads, _ui, () => new YtDlpRunner(), () => AppSettings.Load().MaxConcurrentDownloads);
            _queue.ItemStarted += OnQueueItemStarted;
            _queue.ItemChanged += _ => UpdateActiveCount();
            _queue.ItemCompleted += OnQueueItemCompleted;
            _queue.Drained += () => StatusText = "All downloads complete";

            _history = new HistorySaver(_ui, () => _downloads.Select(d => d.Task).ToList());
            _setup = new EngineSetup(_engine, _queue, _downloads, _ui, _clipboard, text => StatusText = text);

            InitializeCrashReporting();
            AppLogger.Log("[VM] Initializing MainViewModel...");
            
            Settings = AppSettings.Load();
            
            _tray.ShowWindowRequested += (s, e) => RequestShowWindow?.Invoke(this, EventArgs.Empty);
            _tray.ExitRequested += (s, e) => ExitApplication();
            _notifier.Activated += (s, e) => RequestShowWindow?.Invoke(this, EventArgs.Empty);
            _tray.Initialize();

            ProcessUrlCommand = new RelayCommand<string>(ProcessUrl);
            // Stopping a download can take a moment (the process tree must exit), so these three are async and the UI stays
            // responsive meanwhile. AllowConcurrentExecutions keeps each row independent (pausing one does not disable the others).
            PauseDownloadCommand = new AsyncRelayCommand<DownloadItemViewModel>(PauseDownloadAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
            CancelDownloadCommand = new AsyncRelayCommand<DownloadItemViewModel>(CancelDownloadAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
            ResumeDownloadCommand = new RelayCommand<DownloadItemViewModel>(ResumeDownload);
            RemoveDownloadCommand = new AsyncRelayCommand<DownloadItemViewModel>(RemoveDownloadAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
            OpenFolderCommand = new RelayCommand<DownloadItemViewModel>(OpenFolder);
            PlayFileCommand = new RelayCommand<DownloadItemViewModel>(PlayFile);
            
            ClearCompletedCommand = new RelayCommand(ClearCompleted);
            ShowWindowCommand = new RelayCommand(() => RequestShowWindow?.Invoke(this, EventArgs.Empty));
            ExitCommand = new RelayCommand(ExitApplication);
            
            SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
            
            MinimizeToTrayCommand = new RelayCommand(() => 
            {
                IsWindowVisible = false;
                _lifetime.MinimizeMainWindow();
            });

            _downloads.CollectionChanged += (s, e) =>
            {
                HasDownloads = _downloads.Count > 0;
                HasCompletedDownloads = _downloads.Any(d => d.IsCompleted);
                UpdateActiveCount();
                _history.RequestSave();
            };

            LoadPersistedHistory();

            _ = _setup.RunAsync();
        }

        private void InitializeCrashReporting()
        {
            string logDir = AppPaths.Current.LogsDir;
            if (!Directory.Exists(logDir)) try { Directory.CreateDirectory(logDir); } catch { }

            string crashLogPath = Path.Combine(logDir, "crash_log.txt");

            _lifetime.UiUnhandledException += ex =>
                File.WriteAllText(crashLogPath, $"[FATAL UI CRASH] {DateTime.Now}\n{ex}");

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                File.WriteAllText(crashLogPath, $"[FATAL DOMAIN CRASH] {DateTime.Now}\n{e.ExceptionObject}");
            };
        }

        private void TriggerQueueProcessing() => _queue.Trigger();

        private void LoadPersistedHistory()
        {
            var recoveredTasks = HistoryManager.LoadHistory();
            foreach (var task in recoveredTasks)
            {
                var vm = new DownloadItemViewModel(task, _clipboard);
                AttachTaskObserver(vm);
                _downloads.Add(vm);
            }
            if (recoveredTasks.Count > 0)
            {
                AppLogger.Log($"[HISTORY] Successfully recovered {recoveredTasks.Count} tasks from previous session.");
            }
        }

        private void AttachTaskObserver(DownloadItemViewModel vm)
        {
            vm.Task.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DownloadTask.Status) || e.PropertyName == nameof(DownloadTask.ErrorMessage))
                {
                    _history.RequestSave();
                }
            };
        }

        private async Task SaveSettingsAsync()
        {
            Settings.MaxConcurrentDownloads = SettingsRules.ClampConcurrentDownloads(Settings.MaxConcurrentDownloads);

            if (!SettingsRules.TryNormalizeSpeedLimit(Settings.SpeedLimit, out var speed))
            {
                _dialogs.Show("YT Downloader Pro - Settings Error", "Invalid speed limit format.\n\nPlease use '0' for unlimited, or a number followed by K, M, or G (e.g., 500K, 2.5M, 10M).", DialogKind.Warning);

                Settings.SpeedLimit = AppSettings.Load().SpeedLimit;
                OnPropertyChanged(nameof(Settings));
                return;
            }

            Settings.SpeedLimit = speed;

            var oldSettings = AppSettings.Load();
            bool speedChanged = oldSettings.SpeedLimit != Settings.SpeedLimit;
            bool maxChanged = oldSettings.MaxConcurrentDownloads != Settings.MaxConcurrentDownloads;

            Settings.Save();
            OnPropertyChanged(nameof(Settings));

            await _queue.ApplySettingsAsync(speedChanged, maxChanged, Settings.MaxConcurrentDownloads);
        }

        private static bool IsSetupCard(DownloadItemViewModel? vm) => vm?.Task.Title == EngineSetup.SetupCardTitle;

        public void ProcessUrl(string? rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return;

            if (_setup.HasFailed)
            {
                _dialogs.Show("Setup Required", "YT Downloader Pro cannot process links because the initial core setup failed.\n\nPlease ensure you have an active internet connection and restart the application.", DialogKind.Error);
                return;
            }

            try
            {
                // Decoding and validation live in CommandIngest (the payload is untrusted input).
                var parsed = _ingest.Parse(rawUrl);

                if (parsed.Status == IngestStatus.Ignored) return;

                if (parsed.Status == IngestStatus.Blocked)
                {
                    StatusText = "Security Error: Blocked potentially malicious payload.";
                    _dialogs.Show("YT Downloader Pro - Security Alert", $"A download command was blocked for your security.\n\nReason: {parsed.BlockReason}", DialogKind.Warning);
                    return;
                }

                var payload = parsed.Payload!;
                string command = payload.Command;

                lock (_duplicateLock)
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
                        return;
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
                    AttachTaskObserver(vm);

                    _ui.Invoke(() =>
                    {
                        _downloads.Add(vm);
                        HasDownloads = true;
                        StatusText = $"Added: {vm.DisplayTitle}";
                    });
                }

                UpdateActiveCount();
                AppLogger.Log($"[QUEUE] Download task assigned to queue.");

                TriggerQueueProcessing();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[QUEUE ERROR] Payload execution fault: {ex.Message}");
            }
        }

        private void OnQueueItemStarted(DownloadItemViewModel vm)
        {
            SelectedItem = vm;
            StatusText = $"Downloading: {vm.DisplayTitle}";
            RequestScrollToItem?.Invoke(this, vm);
            UpdateActiveCount();
        }

        private void OnQueueItemCompleted(DownloadItemViewModel vm, string title)
        {
            HasCompletedDownloads = true;
            UpdateActiveCount();
            _notifier.NotifyDownloadComplete(title);

            _ = Task.Run(() =>
            {
                DownloadCleaner.CleanupPartialFiles(vm.Task);
                DownloadCleaner.ScrubStrayThumbnails(vm.Task);
            });
        }

        private async Task PauseDownloadAsync(DownloadItemViewModel? vm)
        {
            if (vm == null || IsSetupCard(vm)) return;
            AppLogger.Log($"[QUEUE] Process suspended for task ID: {vm.Id}");
            await _queue.StopAsync(vm.Id);
            vm.Task.Status = DownloadStatus.Paused;
            vm.Refresh();
            UpdateActiveCount();
        }

        private async Task CancelDownloadAsync(DownloadItemViewModel? vm)
        {
            if (vm == null || IsSetupCard(vm)) return;
            AppLogger.Log($"[QUEUE] Process termination requested for task ID: {vm.Id}");
            await _queue.StopAsync(vm.Id);
            vm.Task.Status = DownloadStatus.Cancelled;
            vm.Refresh();
            UpdateActiveCount();
            
            _ = Task.Run(() => DownloadCleaner.CleanupPartialFiles(vm.Task, forceDeleteAll: true));
            DownloadCleaner.CleanupCookieFile(vm.Task);
        }

        private void ResumeDownload(DownloadItemViewModel? vm)
        {
            if (vm == null || IsSetupCard(vm)) return;
            AppLogger.Log($"[QUEUE] Process resumption initiated for task ID: {vm.Id}");
            vm.Task.Status = DownloadStatus.Queued;
            vm.Task.ErrorMessage = "";
            vm.Refresh();
            UpdateActiveCount();
            TriggerQueueProcessing();
        }

        private async Task RemoveDownloadAsync(DownloadItemViewModel? vm)
        {
            if (vm == null || IsSetupCard(vm)) return;
            AppLogger.Log($"[QUEUE] Task purged from registry. ID: {vm.Id}");
            await _queue.StopAsync(vm.Id);
            DownloadCleaner.CleanupCookieFile(vm.Task);
            _downloads.Remove(vm);
            UpdateActiveCount();
        }

        private void OpenFolder(DownloadItemViewModel? vm)
        {
            if (vm?.Task == null || IsSetupCard(vm)) return;
            _opener.ShowInFolder(vm.Task);
        }

        private void PlayFile(DownloadItemViewModel? vm)
        {
            if (vm?.Task == null || IsSetupCard(vm)) return;
            _opener.Play(vm.Task);
        }

        private void ClearCompleted()
        {
            var completed = _downloads.Where(d => d.Task.Status == DownloadStatus.Completed).ToList();
            foreach (var vm in completed) { DownloadCleaner.CleanupCookieFile(vm.Task); _downloads.Remove(vm); }
            HasCompletedDownloads = _downloads.Any(d => d.Task.Status == DownloadStatus.Completed);
            UpdateActiveCount();
            AppLogger.Log("[QUEUE] Completed tasks successfully purged from collection.");
        }

        private void UpdateActiveCount()
        {
            _ui.Post(() =>
            {
                ActiveDownloadCount = _downloads.Count(d => d.Task.Status == DownloadStatus.Queued || d.Task.Status == DownloadStatus.Downloading);
                _tray.UpdateTooltip(ActiveDownloadCount == 0 ? "YT Downloader Pro - Idle" : $"YT Downloader Pro - {ActiveDownloadCount} active");
            });
        }

        private void ExitApplication()
        {
            AppLogger.Log("[SYSTEM] Application termination sequence invoked.");
            AppLogger.Shutdown();
            foreach (var vm in _downloads) DownloadCleaner.CleanupCookieFile(vm.Task);
            _queue.CancelAll();
            _lifetime.Shutdown();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _history.Dispose(); // writes a save that is still waiting
            AppLogger.Shutdown();
            _queue.Dispose();
            foreach (var vm in _downloads) DownloadCleaner.CleanupCookieFile(vm.Task);
            _tray.Dispose();
        }
    }
}
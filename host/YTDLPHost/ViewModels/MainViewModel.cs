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
        private static readonly Regex CommandPathRegex = new(@"-(?:o|P)\s+""([^""]+)""", RegexOptions.Compiled);

        private static readonly HttpClient _httpClient = CreateConfiguredHttpClient();

        private readonly ObservableCollection<DownloadItemViewModel> _downloads = new();
        private readonly object _duplicateLock = new(); 
        
        public AppSettings Settings { get; }

        private bool _disposed;
        private bool _hasDependencyError;

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

            InitializeCrashReporting();
            AppLogger.Log("[VM] Initializing MainViewModel...");
            
            Settings = AppSettings.Load();
            
            _tray.ShowWindowRequested += (s, e) => RequestShowWindow?.Invoke(this, EventArgs.Empty);
            _tray.ExitRequested += (s, e) => ExitCommand.Execute(null);
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
            
            SaveSettingsCommand = new RelayCommand(async () => 
            {
                if (Settings.MaxConcurrentDownloads < 1) Settings.MaxConcurrentDownloads = 1;
                if (Settings.MaxConcurrentDownloads > 15) Settings.MaxConcurrentDownloads = 15;

                string speed = Settings.SpeedLimit?.Trim() ?? "0";
                if (speed != "0" && !Regex.IsMatch(speed, @"^\d+(\.\d+)?[KMG]$", RegexOptions.IgnoreCase))
                {
                    _dialogs.Show("YT Downloader Pro - Settings Error", "Invalid speed limit format.\n\nPlease use '0' for unlimited, or a number followed by K, M, or G (e.g., 500K, 2.5M, 10M).", DialogKind.Warning);
                    
                    var safeSettings = AppSettings.Load();
                    Settings.SpeedLimit = safeSettings.SpeedLimit;
                    OnPropertyChanged(nameof(Settings));
                    return; 
                }
                
                Settings.SpeedLimit = speed.Replace(" ", "").ToUpperInvariant();

                var oldSettings = AppSettings.Load(); 
                bool speedChanged = oldSettings.SpeedLimit != Settings.SpeedLimit;
                bool maxChanged = oldSettings.MaxConcurrentDownloads != Settings.MaxConcurrentDownloads;
                
                Settings.Save();
                OnPropertyChanged(nameof(Settings)); 

                if (speedChanged)
                {
                    AppLogger.Log("[SETTINGS] Speed limit modified. Suspending active tasks to apply new throttle rules...");
                    var activeVms = _downloads.Where(d => d.Task.Status == DownloadStatus.Downloading).ToList();
                    foreach(var vm in activeVms)
                    {
                        await _queue.SuspendToQueueAsync(vm);
                    }
                }
                else if (maxChanged && _queue.ActiveCount > Settings.MaxConcurrentDownloads)
                {
                    AppLogger.Log("[SETTINGS] Max concurrent slots reduced. Suspending excess tasks...");
                    int excess = _queue.ActiveCount - Settings.MaxConcurrentDownloads;
                    
                    var activeVms = _downloads.Where(d => d.Task.Status == DownloadStatus.Downloading)
                                              .Reverse() 
                                              .Take(excess).ToList();
                    foreach(var vm in activeVms)
                    {
                        await _queue.SuspendToQueueAsync(vm);
                    }
                }

                TriggerQueueProcessing();
            });
            
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
                PersistHistory();
            };

            LoadPersistedHistory();

            _ = CheckAndDownloadDependenciesAsync();
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

        private CancellationTokenSource? _saveHistoryCts;
        private void PersistHistory()
        {
            _saveHistoryCts?.Cancel();
            _saveHistoryCts = new CancellationTokenSource();
            var token = _saveHistoryCts.Token;

            List<DownloadTask> snapshot = new();
            _ui.Invoke(() =>
            {
                snapshot = _downloads.Select(d => d.Task).ToList();
            });

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2500, token); 
                    if (!token.IsCancellationRequested)
                    {
                        HistoryManager.SaveHistory(snapshot);
                    }
                }
                catch (TaskCanceledException) { }
            });
        }

        private void AttachTaskObserver(DownloadItemViewModel vm)
        {
            vm.Task.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DownloadTask.Status) || e.PropertyName == nameof(DownloadTask.ErrorMessage))
                {
                    PersistHistory();
                }
            };
        }

        private static string StageText(EngineStage stage) => stage switch
        {
            EngineStage.DownloadingYtDlp => "Downloading yt-dlp engine...",
            EngineStage.DownloadingFfmpeg => "Downloading FFmpeg media codecs...",
            EngineStage.ExtractingFfmpeg => "Extracting codecs...",
            EngineStage.DownloadingDeno => "Downloading JS engine...",
            EngineStage.ExtractingDeno => "Extracting JS engine...",
            _ => "Setting up..."
        };

        private async Task CheckAndDownloadDependenciesAsync()
        {
            DownloadItemViewModel? setupVm = null;

            try
            {
                if (_engine.AllPresent())
                {
                    AppLogger.Log("[DEPENDENCIES] Core dependencies located securely.");
                    _queue.MarkEngineReady();
                    _engine.StartBackgroundUpdate();
                    TriggerQueueProcessing();
                    return;
                }

                AppLogger.Log("[DEPENDENCIES] Dependencies missing. Creating UX Setup Card.");

                var setupTask = new DownloadTask
                {
                    Id = Guid.NewGuid(),
                    Title = "Initial System Setup",
                    Status = DownloadStatus.Downloading,
                    CurrentPhase = "Initializing download...",
                    IsIndeterminate = true
                };
                setupVm = new DownloadItemViewModel(setupTask, _clipboard);

                _ui.Invoke(() =>
                {
                    _downloads.Insert(0, setupVm);
                    StatusText = "Downloading required engine updates... Please wait.";
                });

                await _engine.InstallMissingAsync(stage => _ui.Invoke(() =>
                {
                    if (setupVm != null) { setupVm.Task.CurrentPhase = StageText(stage); setupVm.Refresh(); }
                }));

                _ui.Invoke(() =>
                {
                    if (setupVm != null)
                    {
                        setupVm.Task.CurrentPhase = "Setup Complete!";
                        setupVm.Task.Status = DownloadStatus.Completed;
                        setupVm.Task.Progress = 100.0;
                        setupVm.Task.IsIndeterminate = false;
                        setupVm.Refresh();
                    }
                    StatusText = "Ready";
                });

                await Task.Delay(2000);
                _ui.Invoke(() =>
                {
                    if (setupVm != null) _downloads.Remove(setupVm);
                });

                _queue.MarkEngineReady();
                _engine.StartBackgroundUpdate();
                TriggerQueueProcessing();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[DEPENDENCIES ERROR] Failed to provision dependencies: {ex.Message}");
                _hasDependencyError = true;

                _ui.Invoke(() =>
                {
                    StatusText = "Network Error. Please check your internet connection.";
                    if (setupVm != null)
                    {
                        setupVm.Task.Status = DownloadStatus.Error;
                        setupVm.Task.CurrentPhase = "Setup Failed! Please restart the app.";
                        setupVm.Task.IsIndeterminate = false;
                        setupVm.Task.ErrorMessage = "Check your internet connection and try again.";
                        setupVm.Refresh();
                    }
                });
            }
        }

        public void ProcessUrl(string? rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return;

            if (_hasDependencyError)
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
                        CookiePayload = prepared.CookieContent,
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
                CleanupPartialFiles(vm.Task);
                ScrubStrayThumbnails(vm.Task);
            });
        }

        private void ScrubStrayThumbnails(DownloadTask task)
        {
            try 
            {
                if (string.IsNullOrEmpty(task.OutputPath)) return;
                string dir = Path.GetDirectoryName(task.OutputPath) ?? "";
                string title = Path.GetFileNameWithoutExtension(task.OutputPath) ?? "";
                
                if (Directory.Exists(dir) && !string.IsNullOrEmpty(title))
                {
                    var files = Directory.GetFiles(dir, $"{title}*");
                    string outExt = Path.GetExtension(task.OutputPath).ToLowerInvariant();
                    
                    foreach (var file in files)
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if ((ext == ".webp" || ext == ".jpg" || ext == ".jpeg" || ext == ".png") && ext != outExt)
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
            } 
            catch { }
        }

        private async Task PauseDownloadAsync(DownloadItemViewModel? vm)
        {
            if (vm == null || vm.Task.Title == "Initial System Setup") return;
            AppLogger.Log($"[QUEUE] Process suspended for task ID: {vm.Id}");
            await _queue.StopAsync(vm.Id);
            vm.Task.Status = DownloadStatus.Paused;
            vm.Refresh();
            UpdateActiveCount();
        }

        private async Task CancelDownloadAsync(DownloadItemViewModel? vm)
        {
            if (vm == null || vm.Task.Title == "Initial System Setup") return;
            AppLogger.Log($"[QUEUE] Process termination requested for task ID: {vm.Id}");
            await _queue.StopAsync(vm.Id);
            vm.Task.Status = DownloadStatus.Cancelled;
            vm.Refresh();
            UpdateActiveCount();
            
            _ = Task.Run(() => CleanupPartialFiles(vm.Task, forceDeleteAll: true));
            CleanupCookieFile(vm.Task);
        }

        private void CleanupPartialFiles(DownloadTask task, bool forceDeleteAll = false)
        {
            var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // FIX: bulk jobs ("download all uploads", playlists). Cancelling used to wipe EVERY file the job had produced, because the
            // tracked list spanned all items. The runner now moves finished items to task.SettledFiles; TrackedFiles holds only the item
            // that is downloading right now. On a bulk cancel we additionally never delete a file that is already complete - only
            // partial / intermediate leftovers of that one in-flight item.
            bool isBulk = !string.IsNullOrEmpty(task.PlaylistInfo);
            var settled = new HashSet<string>(task.SettledFiles, StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in task.TrackedFiles.Distinct().ToList())
            {
                if (settled.Contains(filePath)) continue;
                if (!forceDeleteAll && filePath.Equals(task.OutputPath, StringComparison.OrdinalIgnoreCase)) continue;

                var fileName = Path.GetFileName(filePath);
                // Per-format intermediates of a multi-stream download ("Title.f137.mp4", "Title.f251.webm") are never a deliverable.
                bool isIntermediate = Regex.IsMatch(fileName, @"\.f[\w-]+\.[^.]+$");

                if (forceDeleteAll && isBulk && !isIntermediate)
                {
                    // A completed file of the in-flight item (e.g. cancelled while yt-dlp sleeps before the next item) is kept;
                    // only its unfinished ".part"/".ytdl" companions are removed.
                    TryDeleteFile(filePath + ".part");
                    TryDeleteFile(filePath + ".ytdl");
                }
                else
                {
                    // The exact file yt-dlp reported as its destination for this stream.
                    TryDeleteFile(filePath);
                    // While a stream is still downloading (exactly the state a cancelled task is in), yt-dlp writes to "<file>.part"
                    // and sometimes a matching "<file>.ytdl" resume-info file; the final name usually doesn't exist yet.
                    TryDeleteFile(filePath + ".part");
                    TryDeleteFile(filePath + ".ytdl");
                }

                var dir = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(dir)) continue;
                dirs.Add(dir);

                // Multi-stream downloads get per-format suffixes, and task.OutputPath only holds the LAST reported destination, so the
                // leftover sweep below derives a stem from every tracked file.
                var stem = Regex.Replace(fileName, @"\.f[\w-]+\.[^.]+$", "");
                stem = Path.GetFileNameWithoutExtension(stem);
                if (!string.IsNullOrEmpty(stem)) stems.Add(stem);
            }

            if (!forceDeleteAll) return;

            // Belt-and-braces sweep: leftover fragment/resume files of THIS in-flight item only. Settled items are never swept - a
            // short stem such as "Song" would otherwise also match the finished "Song (remix)" next to it.
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var stem in stems)
                {
                    string[] files;
                    try { files = Directory.GetFiles(dir, $"{stem}*"); }
                    catch { continue; }

                    foreach (var file in files)
                    {
                        if (settled.Contains(file)) continue;
                        if (file.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".frag", StringComparison.OrdinalIgnoreCase) ||
                            file.Contains(".part-Frag", StringComparison.OrdinalIgnoreCase) ||
                            file.Contains(".temp.", StringComparison.OrdinalIgnoreCase))
                        {
                            TryDeleteFile(file);
                        }
                    }
                }
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private void ResumeDownload(DownloadItemViewModel? vm)
        {
            if (vm == null || vm.Task.Title == "Initial System Setup") return;
            AppLogger.Log($"[QUEUE] Process resumption initiated for task ID: {vm.Id}");
            vm.Task.Status = DownloadStatus.Queued;
            vm.Task.ErrorMessage = "";
            vm.Refresh();
            UpdateActiveCount();
            TriggerQueueProcessing();
        }

        private async Task RemoveDownloadAsync(DownloadItemViewModel? vm)
        {
            if (vm == null || vm.Task.Title == "Initial System Setup") return;
            AppLogger.Log($"[QUEUE] Task purged from registry. ID: {vm.Id}");
            await _queue.StopAsync(vm.Id);
            CleanupCookieFile(vm.Task);
            _downloads.Remove(vm);
            UpdateActiveCount();
        }

        private void OpenFolder(DownloadItemViewModel? vm)
        {
            if (vm?.Task == null || vm.Task.Title == "Initial System Setup") return;
            _opener.ShowInFolder(vm.Task);
        }

        private void PlayFile(DownloadItemViewModel? vm)
        {
            if (vm?.Task == null || vm.Task.Title == "Initial System Setup") return;
            _opener.Play(vm.Task);
        }

        private void ClearCompleted()
        {
            var completed = _downloads.Where(d => d.Task.Status == DownloadStatus.Completed).ToList();
            foreach (var vm in completed) { CleanupCookieFile(vm.Task); _downloads.Remove(vm); }
            HasCompletedDownloads = _downloads.Any(d => d.Task.Status == DownloadStatus.Completed);
            UpdateActiveCount();
            AppLogger.Log("[QUEUE] Completed tasks successfully purged from collection.");
        }

        private static void CleanupCookieFile(DownloadTask task)
        {
            if (!string.IsNullOrEmpty(task.CookieFilePath) && File.Exists(task.CookieFilePath)) try { File.Delete(task.CookieFilePath); } catch { }
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
            foreach (var vm in _downloads) CleanupCookieFile(vm.Task);
            _queue.CancelAll();
            _lifetime.Shutdown();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _saveHistoryCts?.Cancel();
            _saveHistoryCts?.Dispose(); 
            AppLogger.Shutdown();
            _queue.Dispose();
            foreach (var vm in _downloads) CleanupCookieFile(vm.Task);
            _tray.Dispose();
        }
    }
}
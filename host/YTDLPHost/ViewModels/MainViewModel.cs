using System;
using System.Collections.Concurrent;
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

        private readonly TrayIconService _trayService;
        private readonly ObservableCollection<DownloadItemViewModel> _downloads = new();
        private readonly ConcurrentDictionary<Guid, YtDlpRunner> _activeRunners = new();
        private readonly object _duplicateLock = new(); 
        
        public AppSettings Settings { get; }

        private bool _isProcessingQueue;
        private bool _queueNeedsRestart; 
        private bool _disposed;
        private bool _isDependenciesReady;
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
        private readonly EngineProvisioner _engine;
        private readonly CommandIngest _ingest;

        public MainViewModel(IUiDispatcher ui, IDialogService dialogs, IAppLifetime lifetime)
        {
            _ui = ui;
            _dialogs = dialogs;
            _lifetime = lifetime;
            _engine = new EngineProvisioner(AppPaths.Current, _httpClient);
            _ingest = new CommandIngest(AppPaths.Current);

            InitializeCrashReporting();
            AppLogger.Log("[VM] Initializing MainViewModel...");
            
            Settings = AppSettings.Load();
            
            _trayService = new TrayIconService();
            _trayService.ShowWindowRequested += (s, e) => RequestShowWindow?.Invoke(this, EventArgs.Empty);
            _trayService.ExitRequested += (s, e) => ExitCommand.Execute(null);
            _trayService.Initialize();

            ProcessUrlCommand = new RelayCommand<string>(ProcessUrl);
            PauseDownloadCommand = new RelayCommand<DownloadItemViewModel>(PauseDownload);
            CancelDownloadCommand = new RelayCommand<DownloadItemViewModel>(CancelDownload);
            ResumeDownloadCommand = new RelayCommand<DownloadItemViewModel>(ResumeDownload);
            RemoveDownloadCommand = new RelayCommand<DownloadItemViewModel>(RemoveDownload);
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
                        await SuspendToQueueAsync(vm);
                    }
                }
                else if (maxChanged && _activeRunners.Count > Settings.MaxConcurrentDownloads)
                {
                    AppLogger.Log("[SETTINGS] Max concurrent slots reduced. Suspending excess tasks...");
                    int excess = _activeRunners.Count - Settings.MaxConcurrentDownloads;
                    
                    var activeVms = _downloads.Where(d => d.Task.Status == DownloadStatus.Downloading)
                                              .Reverse() 
                                              .Take(excess).ToList();
                    foreach(var vm in activeVms)
                    {
                        await SuspendToQueueAsync(vm);
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

        private void TriggerQueueProcessing()
        {
            if (_isProcessingQueue)
            {
                _queueNeedsRestart = true;
                return;
            }
            _ = ProcessQueueAsync();
        }

        private async Task SuspendToQueueAsync(DownloadItemViewModel vm)
        {
            AppLogger.Log($"[QUEUE] Hot-swapping task ID: {vm.Id} to apply dynamic settings.");
            
            _ui.Invoke(() => 
            {
                vm.Task.CurrentPhase = "Applying Settings...";
                vm.Refresh();
            });

            if (_activeRunners.TryRemove(vm.Id, out var runner))
            {
                await Task.Run(() => runner.Cancel()); 
            }
            
            _ui.Invoke(() => 
            {
                if (vm.Task.Status != DownloadStatus.Cancelled && vm.Task.Status != DownloadStatus.Error && vm.Task.Status != DownloadStatus.Completed)
                {
                    vm.Task.Status = DownloadStatus.Queued;
                    vm.Refresh();
                    UpdateActiveCount();
                }
            });
        }

        private void LoadPersistedHistory()
        {
            var recoveredTasks = HistoryManager.LoadHistory();
            foreach (var task in recoveredTasks)
            {
                var vm = new DownloadItemViewModel(task);
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
                    _isDependenciesReady = true;
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
                setupVm = new DownloadItemViewModel(setupTask);

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

                _isDependenciesReady = true;
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

                    var vm = new DownloadItemViewModel(task);
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

        private async Task ProcessQueueAsync()
        {
            if (_isProcessingQueue) return;
            _isProcessingQueue = true;

            try
            {
                while (!_disposed)
                {
                    _queueNeedsRestart = false; 

                    if (!_isDependenciesReady) break;

                    var savedSettings = AppSettings.Load();
                    int maxAllowed = savedSettings.MaxConcurrentDownloads;

                    bool startedAny = false;

                    while (_activeRunners.Count < maxAllowed)
                    {
                        DownloadItemViewModel? nextItem = null;

                        _ui.Invoke(() => 
                        {
                            var activeVideoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var d in _downloads)
                            {
                                if (d.Task.Status == DownloadStatus.Downloading)
                                {
                                    activeVideoIds.Add(CommandInfo.ExtractVideoId(d.Task.Command));
                                }
                            }

                            foreach (var d in _downloads)
                            {
                                if (d.Task.Status == DownloadStatus.Queued)
                                {
                                    string vidId = CommandInfo.ExtractVideoId(d.Task.Command);
                                    if (activeVideoIds.Contains(vidId))
                                    {
                                        if (d.Task.CurrentPhase != "Waiting (Similar video active)...")
                                        {
                                            d.Task.CurrentPhase = "Waiting (Similar video active)...";
                                            d.Refresh();
                                        }
                                    }
                                    else if (d.Task.CurrentPhase == "Waiting (Similar video active)...")
                                    {
                                        d.Task.CurrentPhase = "Queued";
                                        d.Refresh();
                                    }
                                }
                            }

                            nextItem = _downloads.FirstOrDefault(d => 
                                d.Task.Status == DownloadStatus.Queued && 
                                !activeVideoIds.Contains(CommandInfo.ExtractVideoId(d.Task.Command)));

                            if (nextItem != null)
                            {
                                nextItem.Task.Status = DownloadStatus.Downloading;
                                nextItem.Task.CurrentPhase = "Starting..."; 
                                nextItem.Refresh(); 
                                SelectedItem = nextItem; 
                                StatusText = $"Downloading: {nextItem.DisplayTitle}"; 
                            }
                        });

                        if (nextItem == null) break;

                        RequestScrollToItem?.Invoke(this, nextItem);
                        UpdateActiveCount();

                        AppLogger.Log($"[QUEUE] Spawning execution runner for task ID: {nextItem.Id}");
                        _ = StartDownloadTaskAsync(nextItem);
                        startedAny = true;

                        await Task.Delay(1000); 
                    }

                    if (!startedAny)
                    {
                        if (_queueNeedsRestart) continue; 
                        break;
                    }
                }

                _ui.Invoke(() =>
                {
                    if (_activeRunners.IsEmpty && !_downloads.Any(d => d.Task.Status == DownloadStatus.Queued)) 
                    {
                        StatusText = "All downloads complete";
                    }
                });
            }
            finally
            {
                _isProcessingQueue = false;
                
                if (_queueNeedsRestart)
                {
                    TriggerQueueProcessing();
                }
            }
        }

        private async Task StartDownloadTaskAsync(DownloadItemViewModel vm)
        {
            var runner = new YtDlpRunner();
            _activeRunners.TryAdd(vm.Id, runner);

            runner.OnProgressUpdate += OnRunnerProgress;
            runner.OnDownloadComplete += OnRunnerComplete;
            runner.OnDownloadError += OnRunnerError;
            runner.OnInfoExtracted += OnRunnerInfo;

            await runner.ExecuteAsync(vm.Task);

            runner.Dispose();
            _activeRunners.TryRemove(vm.Id, out _);

            UpdateActiveCount();
            TriggerQueueProcessing();
        }

        private void OnRunnerProgress(object? sender, ProgressEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null) 
            {
                _ui.Post(UiPriority.Background, () => { vm.Refresh(); UpdateActiveCount(); });
            }
        }

        private void OnRunnerComplete(object? sender, CompleteEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null)
            {
                _ui.Post(UiPriority.Background, () =>
                {
                    vm.Refresh();
                    HasCompletedDownloads = true;
                    UpdateActiveCount();
                    _trayService.ShowDownloadCompleteNotification("Download Complete", e.Title);
                    
                    _ = Task.Run(() => 
                    {
                        CleanupPartialFiles(vm.Task);
                        ScrubStrayThumbnails(vm.Task); 
                    });
                });
            }
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

        private void OnRunnerError(object? sender, DownloadErrorEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null) 
            {
                _ui.Post(UiPriority.Background, () => { vm.Refresh(); UpdateActiveCount(); });
            }
        }

        private void OnRunnerInfo(object? sender, ExtractedInfoEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null) 
            {
                _ui.Post(UiPriority.Background, () => vm.Refresh());
            }
        }

        private void PauseDownload(DownloadItemViewModel? vm)
        {
            if (vm == null || vm.Task.Title == "Initial System Setup") return;
            AppLogger.Log($"[QUEUE] Process suspended for task ID: {vm.Id}");
            if (_activeRunners.TryRemove(vm.Id, out var runner)) runner.Cancel();
            vm.Task.Status = DownloadStatus.Paused;
            vm.Refresh();
            UpdateActiveCount();
        }

        private void CancelDownload(DownloadItemViewModel? vm)
        {
            if (vm == null || vm.Task.Title == "Initial System Setup") return;
            AppLogger.Log($"[QUEUE] Process termination requested for task ID: {vm.Id}");
            if (_activeRunners.TryRemove(vm.Id, out var runner)) runner.Cancel();
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

            foreach (var filePath in task.TrackedFiles.Distinct().ToList())
            {
                if (!forceDeleteAll && filePath.Equals(task.OutputPath, StringComparison.OrdinalIgnoreCase)) continue;

                // The exact file yt-dlp reported as its destination for this stream.
                TryDeleteFile(filePath);
                // FIX: while a stream is still downloading (which is exactly the
                // state a cancelled task is in), yt-dlp writes to "<file>.part"
                // and sometimes a matching "<file>.ytdl" resume-info file - the
                // final filename itself usually doesn't exist yet at cancel time,
                // so the check above almost never actually found anything to delete.
                TryDeleteFile(filePath + ".part");
                TryDeleteFile(filePath + ".ytdl");

                var dir = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(dir)) continue;
                dirs.Add(dir);

                // FIX: multi-stream downloads (separate video/audio before
                // merging) get per-format suffixes like "Title.f137.mp4" and
                // "Title.f251.webm". task.OutputPath only ever holds whichever
                // stream's "Destination:" line was reported LAST, so building
                // the wildcard sweep below from OutputPath alone (the old
                // behavior) would only ever catch ONE stream's leftovers and
                // silently miss the other's - deriving a stem from every
                // tracked file instead catches all of them.
                var fileName = Path.GetFileName(filePath);
                var stem = Regex.Replace(fileName, @"\.f[\w-]+\.[^.]+$", "");
                stem = Path.GetFileNameWithoutExtension(stem);
                if (!string.IsNullOrEmpty(stem)) stems.Add(stem);
            }

            if (!forceDeleteAll) return;

            // Belt-and-braces sweep: anything left in the same folder(s) that
            // still matches one of this task's title stems and looks like a
            // leftover fragment/resume file yt-dlp didn't clean up itself.
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
                        if (file.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".frag", StringComparison.OrdinalIgnoreCase) ||
                            file.Contains(".part-Frag", StringComparison.OrdinalIgnoreCase))
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

        private void RemoveDownload(DownloadItemViewModel? vm)
        {
            if (vm == null || vm.Task.Title == "Initial System Setup") return;
            AppLogger.Log($"[QUEUE] Task purged from registry. ID: {vm.Id}");
            if (_activeRunners.TryRemove(vm.Id, out var runner)) runner.Cancel();
            CleanupCookieFile(vm.Task);
            _downloads.Remove(vm);
            UpdateActiveCount();
        }

        private static void OpenFolder(DownloadItemViewModel? vm)
        {
            if (vm?.Task == null || vm.Task.Title == "Initial System Setup") return;
            var path = vm.Task.OutputPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true }); return; }
            if (!string.IsNullOrEmpty(path)) { var dir = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true }); return; } }
            var saveDir = ExtractSaveDirectory(vm.Task.Command);
            if (Directory.Exists(saveDir)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = saveDir, UseShellExecute = true });
        }

        private static void PlayFile(DownloadItemViewModel? vm)
        {
            if (vm?.Task == null || vm.Task.Title == "Initial System Setup") return;
            var path = vm.Task.OutputPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }); return; }
            OpenFolder(vm);
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
                _trayService.UpdateTooltip(ActiveDownloadCount == 0 ? "YT Downloader Pro - Idle" : $"YT Downloader Pro - {ActiveDownloadCount} active");
            });
        }

        private static string ExtractSaveDirectory(string command)
        {
            var match = CommandInfo.OutputTemplateRegex.Match(command);
            if (match.Success)
            {
                var template = match.Groups[1].Value;
                var dir = Path.GetDirectoryName(template);
                if (!string.IsNullOrEmpty(dir))
                {
                    dir = dir.Replace("/", "\\");
                    dir = Environment.ExpandEnvironmentVariables(dir);
                    if (dir.StartsWith("~\\") || dir.StartsWith("~")) dir = Path.Combine(AppPaths.Current.HomeDir, dir.Substring(1).TrimStart('\\'));
                    if (!Directory.Exists(dir)) try { Directory.CreateDirectory(dir); } catch { }
                    if (Directory.Exists(dir)) return dir;
                }
            }
            return AppPaths.Current.HomeDir;
        }

        private void ExitApplication()
        {
            AppLogger.Log("[SYSTEM] Application termination sequence invoked.");
            AppLogger.Shutdown();
            foreach (var vm in _downloads) CleanupCookieFile(vm.Task);
            foreach (var runner in _activeRunners.Values) runner.Cancel();
            _lifetime.Shutdown();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _saveHistoryCts?.Cancel();
            _saveHistoryCts?.Dispose(); 
            AppLogger.Shutdown();
            foreach (var runner in _activeRunners.Values) runner.Dispose();
            foreach (var vm in _downloads) CleanupCookieFile(vm.Task);
            _trayService.Dispose();
        }
    }
}
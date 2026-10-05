using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTDLPHost.Models;
using YTDLPHost.Services;

namespace YTDLPHost.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private static readonly Regex CommandPathRegex = new(@"-(?:o|P)\s+""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex OutputTemplateRegex = new(@"-o\s+""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex ResHeightRegex = new(@"height<=?(\d+)", RegexOptions.Compiled);
        private static readonly Regex ResRegex = new(@"(\d+)p", RegexOptions.Compiled);

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

        public MainViewModel()
        {
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
                    System.Windows.MessageBox.Show(
                        "Invalid speed limit format.\n\nPlease use '0' for unlimited, or a number followed by K, M, or G (e.g., 500K, 2.5M, 10M).", 
                        "YT Downloader Pro - Settings Error", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Warning);
                    
                    var safeSettings = AppSettings.Load();
                    Settings.SpeedLimit = safeSettings.SpeedLimit;
                    OnPropertyChanged(nameof(Settings));
                    return; 
                }
                
                Settings.SpeedLimit = speed.Replace(" ", "").ToUpper();

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
                var app = System.Windows.Application.Current;
                if (app != null && app.MainWindow != null)
                {
                    app.MainWindow.WindowState = WindowState.Minimized;
                }
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
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YTDownloaderProEngine");
            if (!Directory.Exists(logDir)) try { Directory.CreateDirectory(logDir); } catch { }

            string crashLogPath = Path.Combine(logDir, "crash_log.txt");

            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.DispatcherUnhandledException += (s, e) =>
                {
                    File.WriteAllText(crashLogPath, $"[FATAL UI CRASH] {DateTime.Now}\n{e.Exception}");
                    e.Handled = false; 
                };
            }

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
            
            System.Windows.Application.Current?.Dispatcher.Invoke(() => 
            {
                vm.Task.CurrentPhase = "Applying Settings...";
                vm.Refresh();
            });

            if (_activeRunners.TryRemove(vm.Id, out var runner))
            {
                await Task.Run(() => runner.Cancel()); 
            }
            
            System.Windows.Application.Current?.Dispatcher.Invoke(() => 
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
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
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

        private async Task<byte[]> DownloadFileWithRetryAsync(HttpClient client, string url, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await client.GetByteArrayAsync(url);
                }
                catch (Exception ex) when (i < maxRetries - 1)
                {
                    AppLogger.Log($"[DEPENDENCIES] Network drop detected on dependency download. Retrying ({i + 1}/{maxRetries})...");
                    await Task.Delay(3000); 
                }
            }
            return await client.GetByteArrayAsync(url); 
        }

        private async Task CheckAndDownloadDependenciesAsync()
        {
            DownloadItemViewModel? setupVm = null;
            
            try
            {
                string engineDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YTDownloaderProEngine");
                
                if (!Directory.Exists(engineDir)) 
                {
                    Directory.CreateDirectory(engineDir);
                }

                string ytdlpPath = Path.Combine(engineDir, "yt-dlp.exe");
                string ffmpegPath = Path.Combine(engineDir, "ffmpeg.exe");
                string denoPath = Path.Combine(engineDir, "deno.exe");

                if (File.Exists(ytdlpPath) && File.Exists(ffmpegPath) && File.Exists(denoPath))
                {
                    AppLogger.Log("[DEPENDENCIES] Core dependencies located securely.");
                    _isDependenciesReady = true;
                    _ = Task.Run(() => UpdateYtDlp(ytdlpPath, engineDir));
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

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    _downloads.Insert(0, setupVm);
                    StatusText = "Downloading required engine updates... Please wait.";
                });
                
                if (!File.Exists(ytdlpPath))
                {
                    AppLogger.Log("[DEPENDENCIES] Downloading yt-dlp binary.");
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => 
                    { 
                        if (setupVm != null) { setupVm.Task.CurrentPhase = "Downloading yt-dlp engine..."; setupVm.Refresh(); }
                    });
                    
                    var ytdlpBytes = await DownloadFileWithRetryAsync(_httpClient, "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
                    await File.WriteAllBytesAsync(ytdlpPath, ytdlpBytes);

                    try { File.Delete(ytdlpPath + ":Zone.Identifier"); } catch { }
                }

                if (!File.Exists(ffmpegPath))
                {
                    AppLogger.Log("[DEPENDENCIES] Downloading FFmpeg build archive.");
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => 
                    { 
                        if (setupVm != null) { setupVm.Task.CurrentPhase = "Downloading FFmpeg media codecs..."; setupVm.Refresh(); }
                    });

                    string zipPath = Path.Combine(Path.GetTempPath(), "ffmpeg.zip");
                    string extractPath = Path.Combine(Path.GetTempPath(), "ffmpeg_ext");
                    
                    var ffmpegBytes = await DownloadFileWithRetryAsync(_httpClient, "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip");
                    await File.WriteAllBytesAsync(zipPath, ffmpegBytes);
                    
                    AppLogger.Log("[DEPENDENCIES] Extracting FFmpeg archive contents.");
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => 
                    { 
                        if (setupVm != null) { setupVm.Task.CurrentPhase = "Extracting codecs..."; setupVm.Refresh(); }
                    });

                    if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                    ZipFile.ExtractToDirectory(zipPath, extractPath);
                    
                    var extFiles = Directory.GetFiles(extractPath, "*.exe", SearchOption.AllDirectories);
                    foreach (var file in extFiles)
                    {
                        string fileName = Path.GetFileName(file);
                        if (fileName.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) || fileName.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            string destPath = Path.Combine(engineDir, fileName);
                            File.Copy(file, destPath, true);
                            try { File.Delete(destPath + ":Zone.Identifier"); } catch { }
                        }
                    }
                    
                    File.Delete(zipPath);
                    Directory.Delete(extractPath, true);
                }

                if (!File.Exists(denoPath))
                {
                    AppLogger.Log("[DEPENDENCIES] Downloading Deno JS engine for EJS puzzle bypass.");
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => 
                    { 
                        if (setupVm != null) { setupVm.Task.CurrentPhase = "Downloading JS engine..."; setupVm.Refresh(); }
                    });

                    string denoZipPath = Path.Combine(Path.GetTempPath(), "deno.zip");
                    string denoExtractPath = Path.Combine(Path.GetTempPath(), "deno_ext");
                    
                    var denoBytes = await DownloadFileWithRetryAsync(_httpClient, "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip");
                    await File.WriteAllBytesAsync(denoZipPath, denoBytes);
                    
                    AppLogger.Log("[DEPENDENCIES] Extracting Deno archive contents.");
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => 
                    { 
                        if (setupVm != null) { setupVm.Task.CurrentPhase = "Extracting JS engine..."; setupVm.Refresh(); }
                    });

                    if (Directory.Exists(denoExtractPath)) Directory.Delete(denoExtractPath, true);
                    ZipFile.ExtractToDirectory(denoZipPath, denoExtractPath);
                    
                    string extractedDeno = Path.Combine(denoExtractPath, "deno.exe");
                    if (File.Exists(extractedDeno))
                    {
                        File.Copy(extractedDeno, denoPath, true);
                        try { File.Delete(denoPath + ":Zone.Identifier"); } catch { }
                    }
                    
                    File.Delete(denoZipPath);
                    Directory.Delete(denoExtractPath, true);
                }

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
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
                System.Windows.Application.Current?.Dispatcher.Invoke(() => 
                {
                    if (setupVm != null) _downloads.Remove(setupVm);
                });

                _isDependenciesReady = true;
                _ = Task.Run(() => UpdateYtDlp(ytdlpPath, engineDir));
                TriggerQueueProcessing();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[DEPENDENCIES ERROR] Failed to provision dependencies: {ex.Message}");
                _hasDependencyError = true;
                
                System.Windows.Application.Current?.Dispatcher.Invoke(() => 
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

        private void UpdateYtDlp(string ytdlpPath, string engineDir)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ytdlpPath,
                    Arguments = "-U",
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = engineDir
                };

                psi.Environment.Remove("WT_SESSION");
                psi.Environment.Remove("WT_PROFILE_ID");

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    // FIX: RedirectStandardInput is never set to true above, so calling
                    // proc.StandardInput used to throw InvalidOperationException here on
                    // every run, get swallowed by the catch block below, and silently
                    // skip the yt-dlp self-update check every single time.
                    var outputTask = proc.StandardOutput.ReadToEndAsync();
                    
                    if (proc.WaitForExit(30000))
                    {
                        string output = outputTask.Result;
                        if (output.Contains("up to date", StringComparison.OrdinalIgnoreCase))
                        {
                            AppLogger.Log("[DEPENDENCIES] yt-dlp is synchronized with the latest release.");
                        }
                        else if (output.Contains("Updated yt-dlp", StringComparison.OrdinalIgnoreCase))
                        {
                            AppLogger.Log("[DEPENDENCIES] yt-dlp successfully patched to the latest version.");
                        }
                    }
                    else
                    {
                        proc.Kill(); 
                        AppLogger.Log("[DEPENDENCIES ERROR] Background update process hung and was terminated.");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[DEPENDENCIES ERROR] Execution of the update sub-process failed: {ex.Message}");
            }
        }

        private string _lastBlockReason = string.Empty;

        /// <summary>Allowlist check (see CommandValidator). The payload comes from a ytdlp:// link, i.e. untrusted input.</summary>
        private bool IsCommandSafe(string command)
        {
            if (CommandValidator.TryValidate(command, out var reason)) return true;
            _lastBlockReason = reason;
            AppLogger.Log($"[SECURITY] Blocked command payload: {reason}");
            return false;
        }

        public void ProcessUrl(string? rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return;

            if (_hasDependencyError)
            {
                System.Windows.MessageBox.Show("YT Downloader Pro cannot process links because the initial core setup failed.\n\nPlease ensure you have an active internet connection and restart the application.", "Setup Required", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                string url = Uri.UnescapeDataString(rawUrl);

                if (!url.StartsWith("ytdlp://", StringComparison.OrdinalIgnoreCase)) return;

                var payload = url.Substring(8).TrimEnd('/');
                var parts = payload.Split(new[] { "||" }, StringSplitOptions.None);

                if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) return;

                var command = DecodeBase64(parts[0]);
                AppLogger.Log("[QUEUE] New command payload successfully parsed.");
                
                if (string.IsNullOrWhiteSpace(command)) return;

                if (!IsCommandSafe(command))
                {
                    StatusText = "Security Error: Blocked potentially malicious payload.";
                    System.Windows.MessageBox.Show($"A download command was blocked for your security.\n\nReason: {_lastBlockReason}", "YT Downloader Pro - Security Alert", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                lock (_duplicateLock)
                {
                    bool isDuplicate = false;
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
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

                    string? cookieContent = null;
                    string? cookieFilePath = null;

                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        try
                        {
                            cookieContent = DecodeBase64(parts[1]);
                            if (!string.IsNullOrWhiteSpace(cookieContent))
                            {
                                cookieContent = cookieContent.TrimStart('\uFEFF');

                                var cookieFile = Path.Combine(Path.GetTempPath(), $"ytdlp_cookies_{Guid.NewGuid()}.txt");
                                File.WriteAllText(cookieFile, cookieContent, new UTF8Encoding(false));
                                cookieFilePath = cookieFile;
                                AppLogger.Log("[COOKIES] Session cookies provisioned to local temporary storage.");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Log($"[COOKIES ERROR] Cookie deserialization failure: {ex.Message}");
                        }
                    }

                    if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
                    {
                        try
                        {
                            string exactUserAgent = DecodeBase64(parts[2]);
                            if (!string.IsNullOrWhiteSpace(exactUserAgent) && !command.Contains("--user-agent"))
                            {
                                command += $" --user-agent \"{exactUserAgent}\"";
                                AppLogger.Log("[COOKIES] Injected native browser User-Agent to match cookies.");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Log($"[COOKIES ERROR] User-Agent deserialization failure: {ex.Message}");
                        }
                    }

                    var task = new DownloadTask
                    {
                        UrlPayload = url,
                        Command = command,
                        CookiePayload = cookieContent,
                        CookieFilePath = cookieFilePath ?? string.Empty,
                        Resolution = ExtractResolution(command),
                        Title = ExtractTitleHint(command),
                        Status = DownloadStatus.Queued
                    };

                    var vm = new DownloadItemViewModel(task);
                    AttachTaskObserver(vm); 
                    
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
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

        private static string ExtractVideoId(string command)
        {
            // YouTube: prefer the canonical 11-char video ID so different URL
            // forms of the same video (watch/shorts/embed/youtu.be) still
            // count as the same content for the "don't run two downloads of
            // this at once" mutex below.
            var ytMatch = Regex.Match(command, @"(?:v=|youtu\.be/|shorts/|embed/)([\w-]{11})");
            if (ytMatch.Success) return "yt:" + ytMatch.Groups[1].Value;

            // FIX: this used to fall back to Guid.NewGuid() here - a fresh,
            // different random value on every single call, even for the
            // exact same command string. Since this method gets called
            // multiple times per queue tick (building the active-set, then
            // checking each queued item), two calls for the *same* task
            // would almost never produce matching IDs, silently disabling
            // the mutex entirely for anything that isn't a bare YouTube
            // video URL - which already included channel/playlist jobs
            // today, and will include every command from every non-YouTube
            // site once the extension supports them. Use the actual target
            // URL (the last quoted argument in the command) instead: it's
            // stable across calls and still distinguishes different
            // videos/channels from each other correctly.
            var urlMatches = Regex.Matches(command, "\"([^\"]+)\"");
            if (urlMatches.Count > 0) return urlMatches[urlMatches.Count - 1].Groups[1].Value;

            return command; // last resort: still a stable key, just a coarse one
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

                        System.Windows.Application.Current?.Dispatcher.Invoke(() => 
                        {
                            var activeVideoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var d in _downloads)
                            {
                                if (d.Task.Status == DownloadStatus.Downloading)
                                {
                                    activeVideoIds.Add(ExtractVideoId(d.Task.Command));
                                }
                            }

                            foreach (var d in _downloads)
                            {
                                if (d.Task.Status == DownloadStatus.Queued)
                                {
                                    string vidId = ExtractVideoId(d.Task.Command);
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
                                !activeVideoIds.Contains(ExtractVideoId(d.Task.Command)));

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

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
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
                _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { vm.Refresh(); UpdateActiveCount(); });
            }
        }

        private void OnRunnerComplete(object? sender, CompleteEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null)
            {
                _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
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
                    string outExt = Path.GetExtension(task.OutputPath).ToLower();
                    
                    foreach (var file in files)
                    {
                        string ext = Path.GetExtension(file).ToLower();
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
                _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { vm.Refresh(); UpdateActiveCount(); });
            }
        }

        private void OnRunnerInfo(object? sender, ExtractedInfoEventArgs e)
        {
            var vm = _downloads.FirstOrDefault(d => d.Id == e.TaskId);
            if (vm != null) 
            {
                _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => vm.Refresh());
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
            _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ActiveDownloadCount = _downloads.Count(d => d.Task.Status == DownloadStatus.Queued || d.Task.Status == DownloadStatus.Downloading);
                _trayService.UpdateTooltip(ActiveDownloadCount == 0 ? "YT Downloader Pro - Idle" : $"YT Downloader Pro - {ActiveDownloadCount} active");
            });
        }

        private static string DecodeBase64(string input)
        {
            string padded = input.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }

        private static string ExtractResolution(string command)
        {
            if (command.Contains("ba") && (command.Contains("extract-audio") || command.Contains("audio"))) return "Audio";
            var match = ResHeightRegex.Match(command);
            if (match.Success) return match.Groups[1].Value + "p";
            match = ResRegex.Match(command);
            if (match.Success) return match.Groups[1].Value + "p";
            return "";
        }

        private static string ExtractTitleHint(string command)
        {
            var match = OutputTemplateRegex.Match(command);
            if (match.Success)
            {
                string fileName = Path.GetFileNameWithoutExtension(match.Groups[1].Value) ?? "Fetching Title...";
                return fileName.Replace("%(title)s", "Fetching Title...").Replace("%(uploader)s", "Channel");
            }
            return "Fetching Title...";
        }

        private static string ExtractSaveDirectory(string command)
        {
            var match = OutputTemplateRegex.Match(command);
            if (match.Success)
            {
                var template = match.Groups[1].Value;
                var dir = Path.GetDirectoryName(template);
                if (!string.IsNullOrEmpty(dir))
                {
                    dir = dir.Replace("/", "\\");
                    dir = Environment.ExpandEnvironmentVariables(dir);
                    if (dir.StartsWith("~\\") || dir.StartsWith("~")) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), dir.Substring(1).TrimStart('\\'));
                    if (!Directory.Exists(dir)) try { Directory.CreateDirectory(dir); } catch { }
                    if (Directory.Exists(dir)) return dir;
                }
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private void ExitApplication()
        {
            AppLogger.Log("[SYSTEM] Application termination sequence invoked.");
            AppLogger.Shutdown();
            foreach (var vm in _downloads) CleanupCookieFile(vm.Task);
            foreach (var runner in _activeRunners.Values) runner.Cancel();
            System.Windows.Application.Current?.Shutdown();
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
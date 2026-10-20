using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YTDLPHost.Models;

namespace YTDLPHost.Services
{
    public class YtDlpRunner : IDisposable
    {
        private static readonly Regex CmdTrimRegex = new(@"^(?:yt-dlp\.exe|yt-dlp)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PercentRegex = new(@"\[download\]\s+(?:(\d+\.?\d*)%|100%)", RegexOptions.Compiled);
        private static readonly Regex SpeedRegex = new(@"at\s+([\d\.]+(?:[KMG]iB|B)/s)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex EtaRegex = new(@"ETA\s+([\d:]+|Unknown)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SizeRegex = new(@"of\s+(~?\s*[\d\.]+\s*(?:[KMG]iB|B))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PlaylistRegex = new(@"Downloading item (\d+) of (\d+)", RegexOptions.Compiled);
        private static readonly Regex OutputTemplateRegex = new(@"-o\s+""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex PathTemplateRegex = new(@"-P\s+""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex FileTrackerRegex = new(@"(?:Destination:|Writing video \w+ to:|Writing video \w+ \d+ to:)\s+(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // The final name of a merged download appears ONLY on this line (never as "Destination:").
        private static readonly Regex MergerOutputRegex = new(@"\[Merger\]\s+Merging formats into\s+""(.+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AlreadyDownloadedRegex = new(@"\[download\]\s+(.*?)\s+has already been downloaded", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ffmpeg writes its stats line to STDERR (yt-dlp hands it the download for --download-sections and for live HLS):
        //   frame= 2148 fps=4.4 q=31.0 size=   30976KiB time=00:00:59.85 bitrate=4239.9kbits/s speed=0.121x elapsed=0:08:13.53
        // (the final line says "Lsize=" instead of "size="). Audio-only runs have no frame/fps, so only size/time are required.
        private static readonly Regex FfmpegStatsRegex = new(@"(?:^|\s)L?size=\s*(?:(?<size>\d+(?:\.\d+)?)\s*(?<unit>[KMGT]?i?B)|N/A)\s+time=(?<h>\d+):(?<m>\d+):(?<s>\d+(?:\.\d+)?)(?:\s+bitrate=\s*[^\s]+)?(?:\s+speed=\s*(?<speed>[\d\.]+)x)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Native live-HLS/DASH recording has no total, so yt-dlp prints no percentage:
        //   [download]   12.50MiB at    2.50MiB/s (00:00:05) (frag 14)
        private static readonly Regex LiveProgressRegex = new(@"\[download\]\s+(?<size>~?\s*[\d\.]+\s*[KMGT]?i?B)\s+at\s+(?<speed>[\d\.]+\s*[KMGT]?i?B/s|Unknown(?: B/s)?)\s+\((?<elapsed>[\d:]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // "[info] 318901763557: Downloading 1 time ranges: 0.0-60.0" - the cut length lets us turn ffmpeg's "time=" into a real percentage.
        private static readonly Regex TimeRangeRegex = new(@"Downloading\s+\d+\s+time ranges?:\s*(?<a>[\d\.]+)-(?<b>[\d\.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SectionArgRegex = new(@"--download-sections\s+""?\*?(?<a>[\d:\.]+)-(?<b>[\d:\.]+)""?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ffmpeg's own chatter on a live recording is thousands of lines: one "Opening 'https://...'" per HLS segment / playlist refresh,
        // stream dumps, corrupt-packet notes... Segment "Opening" lines carry no information, so they are dropped from the log; the other
        // ffmpeg diagnostics go to the on-disk log only so the live log view stays readable.
        private static readonly Regex FfmpegPrefixRegex = new(@"^\[[\w#/:.\-]+\s*@\s*(?:0x)?[0-9A-Fa-f]+\]\s+", RegexOptions.Compiled);
        private static readonly Regex FfmpegOpeningRegex = new(@"^\[[\w#/:.\-]+\s*@\s*(?:0x)?[0-9A-Fa-f]+\]\s+Opening\s+'", RegexOptions.Compiled);
        private static readonly Regex FfmpegBlockRegex = new(@"^(?:Input #\d+|Output #\d+|Stream mapping:|Press \[q\]|\s{2,}\S)", RegexOptions.Compiled);

        // --- YouTube quality guard ---
        // When YouTube withholds the adaptive formats (PO-token / SABR enforcement) yt-dlp silently falls back to a legacy muxed
        // progressive itag. "[info] <id>: Downloading 1 format(s): 18" is the only trace of that, so it is detected and reported.
        private static readonly Regex YoutubeUrlRegex = new(@"https?://(?:[\w-]+\.)*(?:youtube\.com|youtu\.be|youtube-nocookie\.com)/", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FormatPickRegex = new(@"\[info\]\s+\S+:\s+Downloading\s+\d+\s+format\(s\):\s+(?<ids>\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RequestedHeightRegex = new(@"height<=(\d+)", RegexOptions.Compiled);
        private static readonly Regex NoWarningsRegex = new(@"\s--no-warnings(?=\s|$)", RegexOptions.Compiled);
        private static readonly Regex PlayerClientListRegex = new(@"^[A-Za-z0-9_,\-]+$", RegexOptions.Compiled);
        private static readonly Dictionary<string, int> LegacyMuxedHeights = new() { ["17"] = 144, ["36"] = 240, ["18"] = 360, ["22"] = 720 };

        private bool _isYoutube;
        private bool _usedCookies;
        private bool _cookiesLoggedIn;
        private int _requestedHeight;
        private string _qualityNote = "";

        public static bool IsYoutubeCommand(string command) => YoutubeUrlRegex.IsMatch(command);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetACP();

        // The encoding yt-dlp.exe (a PyInstaller build) really uses for piped output: the system ANSI code page (e.g. 1252).
        // .NET only knows it after the CodePages provider is registered; fall back to UTF-8 if anything goes wrong.
        private static Encoding AnsiCodePageEncoding()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding((int)GetACP());
            }
            catch
            {
                return Encoding.UTF8;
            }
        }

        // yt-dlp counts a YouTube session as logged in only if the Netscape cookie file holds one of these.
        private static bool CookieFileHasYoutubeLogin(string path)
        {
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    var cols = line.Split('\t');
                    if (cols.Length >= 7 && (cols[5] is "SAPISID" or "__Secure-3PAPISID" or "__Secure-1PAPISID") && cols[6].Trim().Length > 0)
                        return true;
                }
            }
            catch { }
            return false;
        }

        // "Sign in to confirm you're not a bot" - the apostrophe may arrive garbled, so match the stable parts only.
        private static bool IsBotCheckError(string text) =>
            text.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("not a bot", StringComparison.OrdinalIgnoreCase);

        private Process? _process;
        private readonly CancellationTokenSource _cts = new();
        private readonly StringBuilder _errorBuffer = new();
        
        private readonly Dictionary<string, string> _filePhases = new(StringComparer.OrdinalIgnoreCase);
        
        private bool _extractionComplete;
        private bool _disposed;
        private bool _hasStartedVideoMedia;
        private DateTime _lastUiUpdate = DateTime.MinValue;

        // --- Per-item bookkeeping for bulk jobs (playlists / "Download all uploads") ---
        private int _bulkTotal;               // N from "Downloading item X of N" (0 = single download)
        private int _itemIndex;               // X
        private bool _itemHasMedia;           // current item produced/located a real media file
        private bool _itemAlready;            // ... and it was already on disk
        private bool _freshMedia;             // this run wrote a media file (a "Destination:" line, a merge, an audio extraction)

        /// <summary>
        /// True when a single download finished only because yt-dlp found the target file already on disk ("has already been
        /// downloaded") and wrote nothing new. Shown as a note instead of a plain "Finished Successfully", so a skipped download
        /// (e.g. the same name used by a different clip) can no longer pass for a real one.
        /// </summary>
        internal bool OnlyAlreadyOnDisk => _bulkTotal == 0 && _itemAlready && !_freshMedia;
        private bool _itemError;              // yt-dlp printed "ERROR:" while on the current item
        private bool _itemDrm;                // ... and it was a DRM error
        private int _savedItems, _skippedItems, _failedItems, _drmItems;
        private string _lastErrorLine = "";
        private string _lastGoodTitle = "";
        private readonly object _itemLock = new();

        // --- ffmpeg-driven downloads (crop sections, live streams) ---
        private double _sectionSeconds;       // length of the requested cut, 0 = unknown / open-ended (live)
        private DateTime _lastStatsLog = DateTime.MinValue;
        private bool _loggedFfmpegStart;

        public event EventHandler<ProgressEventArgs>? OnProgressUpdate;
        public event EventHandler<CompleteEventArgs>? OnDownloadComplete;
        public event EventHandler<DownloadErrorEventArgs>? OnDownloadError;
        public event EventHandler<ExtractedInfoEventArgs>? OnInfoExtracted;

        public bool IsRunning => _process != null && !_process.HasExited;

        public async Task ExecuteAsync(DownloadTask task)
        {
            _errorBuffer.Clear();
            _filePhases.Clear();
            _extractionComplete = false;
            _hasStartedVideoMedia = false; 
            _bulkTotal = 0; _itemIndex = 0;
            _itemHasMedia = _itemAlready = _itemError = _itemDrm = false;
            _savedItems = _skippedItems = _failedItems = _drmItems = 0;
            _lastErrorLine = ""; _lastGoodTitle = ""; _freshMedia = false;
            _sectionSeconds = 0; _lastStatsLog = DateTime.MinValue; _loggedFfmpegStart = false;
            _isYoutube = false; _usedCookies = false; _cookiesLoggedIn = false; _requestedHeight = 0; _qualityNote = "";

            task.ClearLog();
            task.CurrentPhase = "Starting...";
            task.IsIndeterminate = true;

            var command = task.Command.Trim();
            command = CmdTrimRegex.Replace(command, "");

            if (command.Contains("-f \"ba\""))
            {
                command = command.Replace("-f \"ba\"", "-f \"ba/b\"");
                AppLogger.Log("[ENGINE] Upgraded strict Audio request to 'ba/b' fallback for Mobile API compatibility.");
            }

            if (command.Contains("--extract-audio") && !command.Contains("--embed-thumbnail"))
            {
                if (!command.Contains("wav", StringComparison.OrdinalIgnoreCase))
                {
                    command += " --embed-thumbnail";
                }
            }

            try
            {
                var saveDirectory = ExtractSaveDirectory(command);

                string engineDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YTDownloaderProEngine");
                string ytdlpPath = Path.Combine(engineDir, "yt-dlp.exe");
                string ffmpegPath = Path.Combine(engineDir, "ffmpeg.exe");
                string denoPath = Path.Combine(engineDir, "deno.exe");

                if (!command.Contains("--ffmpeg-location"))
                {
                    command += $" --ffmpeg-location \"{ffmpegPath}\"";
                }

                if (File.Exists(denoPath) && !command.Contains("--js-runtimes"))
                {
                    command += $" --js-runtimes \"deno:{denoPath}\"";
                }

                var settings = AppSettings.Load();
                if (settings.SpeedLimit != "0" && !command.Contains("--limit-rate"))
                {
                    command += $" --limit-rate {settings.SpeedLimit}";
                }

                _isYoutube = IsYoutubeCommand(command);
                var heightMatch = RequestedHeightRegex.Match(command);
                if (heightMatch.Success) int.TryParse(heightMatch.Groups[1].Value, out _requestedHeight);

                if (_isYoutube)
                {
                    // --no-warnings hides yt-dlp's explanation when YouTube withholds formats ("... require a GVS PO Token ...").
                    // Keep warnings for YouTube so the log says WHY a download came out low quality.
                    command = NoWarningsRegex.Replace(command, "");

                    // Do NOT pin player_client here. The upstream default list is retuned in every yt-dlp release (it already
                    // switches itself when cookies are present); a hard-coded list goes stale and silently caps quality.
                    // The settings.json override exists only as a manual emergency hotfix.
                    var clients = settings.YoutubePlayerClients?.Trim() ?? "";
                    if (clients.Length > 0 && PlayerClientListRegex.IsMatch(clients) && !command.Contains("--extractor-args"))
                    {
                        command += $" --extractor-args \"youtube:player_client={clients}\"";
                        task.AppendLog($"[ENGINE] YouTube player_client override from settings: {clients}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(task.CookiePayload))
                {
                    if (string.IsNullOrWhiteSpace(task.CookieFilePath) || !File.Exists(task.CookieFilePath))
                    {
                        task.CookieFilePath = Path.Combine(Path.GetTempPath(), $"ytdlp_cookies_{Guid.NewGuid()}.txt");
                        File.WriteAllText(task.CookieFilePath, task.CookiePayload, new UTF8Encoding(false));
                    }
                }

                if (!string.IsNullOrEmpty(task.CookieFilePath) && File.Exists(task.CookieFilePath))
                {
                    _usedCookies = true;
                    if (_isYoutube)
                    {
                        _cookiesLoggedIn = CookieFileHasYoutubeLogin(task.CookieFilePath);
                        if (!_cookiesLoggedIn)
                        {
                            task.AppendLog("[ENGINE] The cookie file has no SAPISID / __Secure-3PAPISID cookie, so yt-dlp will treat this as a LOGGED-OUT session " +
                                           "(guest client list, no auth header). Sign in to YouTube in this browser, reload the extension and try again.");
                        }
                    }
                    if (!command.Contains("--cookies"))
                    {
                        command += $" --cookies \"{task.CookieFilePath}\"";
                    }
                }

                var sectionMatch = SectionArgRegex.Match(command);
                if (sectionMatch.Success)
                {
                    _sectionSeconds = Math.Max(0, ParseTimeToSeconds(sectionMatch.Groups["b"].Value) - ParseTimeToSeconds(sectionMatch.Groups["a"].Value));
                }

                task.AppendLog("=== Download Task Started ===");
                
                // FIX: Removed redaction to provide full transparency in logs. (The cookie file path is logged, not the base64 content, so it is safe).
                task.AppendLog($"Command executed: yt-dlp.exe {command}");
                task.AppendLog("");

                var psi = new ProcessStartInfo
                {
                    FileName = ytdlpPath,
                    Arguments = command,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = engineDir,
                    StandardOutputEncoding = Encoding.UTF8,   // replaced with the ANSI code page just below
                    StandardErrorEncoding = Encoding.UTF8
                };

                psi.Environment.Remove("WT_SESSION");
                psi.Environment.Remove("WT_PROFILE_ID");

                // The bundled yt-dlp.exe ignores PYTHONUTF8 / PYTHONIOENCODING and writes piped output in the Windows ANSI code page,
                // so decoding it as UTF-8 turned the typographic apostrophe in "you're" into "you�re". Decode with the same code page.
                psi.StandardOutputEncoding = AnsiCodePageEncoding();
                psi.StandardErrorEncoding = AnsiCodePageEncoding();

                _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _process.OutputDataReceived += (s, e) => HandleOutput(e.Data, task);
                _process.ErrorDataReceived += (s, e) => HandleError(e.Data, task);

                var tcs = new TaskCompletionSource<bool>();
                _process.Exited += (s, e) => tcs.TrySetResult(true);

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                await tcs.Task;

                if (_disposed) return;

                // The Exited event can fire before the last stdout/stderr lines were delivered. Give the readers a moment to drain
                // (bounded - a lingering ffmpeg child could otherwise keep the pipes open) so the per-item bookkeeping below is complete.
                try
                {
                    var proc = _process;
                    if (proc != null) await Task.WhenAny(Task.Run(() => proc.WaitForExit()), Task.Delay(3000));
                }
                catch { }

                if (_disposed) return;

                int exitCode = _process?.ExitCode ?? -1;

                FinishCurrentItem(task, settleFiles: false);
                bool isBulk = _bulkTotal > 0;
                int okItems = _savedItems + _skippedItems;
                string bulkNote = isBulk ? BuildResultNote() : "";

                if (isBulk && task.Title.Contains("Fetching", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_lastGoodTitle))
                {
                    task.Title = _lastGoodTitle;
                }

                // A bulk job (playlist / "download all uploads") that saved at least one item is a success even though yt-dlp
                // exits non-zero because other items failed (DRM-protected, private, geo-blocked...). Those failures are summarised
                // next to "Completed" instead of turning the whole job - and the files it did save - into an "Error".
                bool success = exitCode == 0 ? !(isBulk && okItems == 0 && _failedItems > 0) : (isBulk && okItems > 0);

                if (success)
                {
                    task.Status = DownloadStatus.Completed;
                    task.Progress = 100.0;
                    task.IsIndeterminate = false;
                    task.CurrentPhase = "Complete";
                    task.CompletedAt = DateTime.Now;
                    task.Speed = "";
                    task.Eta = "";
                    string resultNote = _failedItems > 0 || _skippedItems > 0 ? bulkNote : "";
                    if (OnlyAlreadyOnDisk) resultNote = "file already existed, nothing new downloaded";
                    if (_qualityNote.Length > 0) resultNote = resultNote.Length == 0 ? _qualityNote : $"{resultNote}; {_qualityNote}";
                    task.ResultNote = resultNote;
                    task.AppendLog("");
                    task.AppendLog(string.IsNullOrEmpty(task.ResultNote)
                        ? "=== Download Finished Successfully ==="
                        : $"=== Download Finished: {task.ResultNote} ===");

                    OnDownloadComplete?.Invoke(this, new CompleteEventArgs(task.Id, task.OutputPath, task.Title));
                }
                else
                {
                    string lastErrors = string.Join("\n", _errorBuffer.ToString()
                        .Split('\n')
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .TakeLast(3));

                    task.Status = DownloadStatus.Error;
                    task.IsIndeterminate = false;

                    if (_errorBuffer.ToString().Contains("only works when logged-in", StringComparison.OrdinalIgnoreCase))
                    {
                        // yt-dlp's own wording (Vimeo, Oct 2026). Say what to do in plain language; the raw line stays in the log.
                        task.ErrorMessage = "Vimeo needs you to be logged in. Log in to vimeo.com in your browser, reload the video page and try the download again.";
                    }
                    else if (_isYoutube && IsBotCheckError(_errorBuffer.ToString()))
                    {
                        // The raw yt-dlp line stays in the log (and is copyable); this says what to actually do.
                        string errText = _errorBuffer.ToString();
                        bool rateLimited = errText.Contains("429", StringComparison.Ordinal) ||
                                           errText.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);

                        if (!_usedCookies)
                        {
                            task.ErrorMessage = "YouTube is asking for a sign-in (bot check). Turn on \"Bypass Age & Bot Lock\" while you are signed in to YouTube in this browser, then start the download again from the video page.";
                        }
                        else if (!_cookiesLoggedIn)
                        {
                            task.ErrorMessage = "YouTube treated this as a signed-out session: no YouTube login cookie (SAPISID) reached the downloader. Sign in to YouTube in this browser, reload the extension and the video page, then try again.";
                        }
                        else if (rateLimited)
                        {
                            task.ErrorMessage = "YouTube is rate-limiting this connection (HTTP 429), so the bot check was triggered even with your login. Stop retrying for a while (15-60 minutes), avoid VPN/shared networks, then try again.";
                        }
                        else
                        {
                            task.ErrorMessage = "YouTube still asked for a sign-in (bot check) even with your login. Reload the video page and try again; if it keeps happening your session cookies may have been rotated or this network is being flagged.";
                        }
                    }
                    else if (isBulk && _drmItems > 0 && _drmItems == _failedItems)
                    {
                        task.ErrorMessage = $"None of the {_failedItems} item(s) could be downloaded: they are DRM-protected (e.g. SoundCloud Go+ / protected tracks), which yt-dlp cannot download.";
                    }
                    else if (isBulk && _failedItems > 0)
                    {
                        task.ErrorMessage = string.IsNullOrWhiteSpace(_lastErrorLine) ? bulkNote : $"{bulkNote}. Last error: {_lastErrorLine}";
                    }
                    else
                    {
                        task.ErrorMessage = string.IsNullOrWhiteSpace(lastErrors)
                            ? $"Download failed (Code: {exitCode}). Check logs for details."
                            : lastErrors;
                    }
                    
                    task.AppendLog("");
                    task.AppendLog($"=== ERROR (Code: {exitCode}) ===");
                    task.AppendLog(task.ErrorMessage);

                    OnDownloadError?.Invoke(this, new DownloadErrorEventArgs(task.Id, task.ErrorMessage));
                }
            }
            catch (Exception ex)
            {
                if (_disposed) return;
                task.Status = DownloadStatus.Error;
                task.IsIndeterminate = false;
                task.ErrorMessage = $"Execution error: {ex.Message}";
                task.AppendLog($"[DEBUG] Critical Exception: {ex.Message}");
                OnDownloadError?.Invoke(this, new DownloadErrorEventArgs(task.Id, task.ErrorMessage));
            }
            finally
            {
                task.AppendLog($"=== Log End {DateTime.Now} ===");
                CleanupProcess();
                SaveLogsToDisk(task);
            }
        }

        private void HandleOutput(string? data, DownloadTask task)
        {
            if (string.IsNullOrWhiteSpace(data)) return;

            try
            {
                HandleOutputCore(data, task);
            }
            catch (Exception ex)
            {
                // FIX: This callback runs on the process's async I/O thread, outside the
                // Dispatcher's exception handling. An unhandled exception here (bad regex
                // match, unexpected null, etc.) would crash the entire application, not
                // just fail this one download. Log and move on instead.
                AppLogger.Log($"[ENGINE ERROR] Failed to parse yt-dlp output line: {ex.Message}");
            }
        }

        private void HandleOutputCore(string data, DownloadTask task)
        {
            task.AppendLog(data);
            bool needsUiUpdate = false;
            double oldProgress = task.Progress;

            if (_isYoutube && _requestedHeight > 0 && _qualityNote.Length == 0)
            {
                var pick = FormatPickRegex.Match(data);
                if (pick.Success && LegacyMuxedHeights.TryGetValue(pick.Groups["ids"].Value, out var gotHeight) && gotHeight < _requestedHeight)
                {
                    _qualityNote = $"only {gotHeight}p offered by YouTube (wanted {_requestedHeight}p)";
                    task.AppendLog($"[ENGINE] YouTube offered only legacy format {pick.Groups["ids"].Value} ({gotHeight}p) although {_requestedHeight}p was requested. " +
                                   "See the 'PO Token' / 'SABR' warnings above for the reason. Fixes: update yt-dlp (UpdateChannel in settings.json), " +
                                   "or set YoutubePlayerClients there as a temporary override.");
                }
            }

            if (data.Contains("[download] Downloading item", StringComparison.OrdinalIgnoreCase))
            {
                var match = PlaylistRegex.Match(data);
                if (match.Success)
                {
                    // Close the books on the previous item FIRST: its files become "settled" and can never be touched by cleanup again.
                    FinishCurrentItem(task);

                    int.TryParse(match.Groups[1].Value, out _itemIndex);
                    int.TryParse(match.Groups[2].Value, out _bulkTotal);
                    task.PlaylistInfo = BuildPlaylistInfo();
                    
                    _extractionComplete = false; 
                    _hasStartedVideoMedia = false; 
                    _filePhases.Clear(); 
                    task.Progress = 0.0;
                    task.CurrentPhase = "Starting...";
                    task.FileSize = "";
                    task.Speed = "";
                    task.Eta = "";
                    task.Title = "Fetching Title..."; 
                    OnProgressUpdate?.Invoke(this, new ProgressEventArgs(task.Id, task.Progress, task.Speed, task.Eta));
                }
                return; 
            }

            if (data.Contains("time range", StringComparison.OrdinalIgnoreCase))
            {
                var rangeMatch = TimeRangeRegex.Match(data);
                if (rangeMatch.Success &&
                    double.TryParse(rangeMatch.Groups["a"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rangeStart) &&
                    double.TryParse(rangeMatch.Groups["b"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rangeEnd))
                {
                    _sectionSeconds = Math.Max(0, rangeEnd - rangeStart);
                }
            }

            // A merge, audio extraction or remux only happens when this run produced a new file.
            if (data.Contains("[Merger]", StringComparison.OrdinalIgnoreCase) || data.Contains("[ExtractAudio]", StringComparison.OrdinalIgnoreCase) ||
                data.Contains("[VideoRemuxer]", StringComparison.OrdinalIgnoreCase) || data.Contains("[VideoConvertor]", StringComparison.OrdinalIgnoreCase))
            {
                _freshMedia = true;
            }

            if (data.Contains("has already been recorded in the archive", StringComparison.OrdinalIgnoreCase) ||
                data.Contains("does not pass filter", StringComparison.OrdinalIgnoreCase))
            {
                // Skipped by --download-archive / a match filter: counts as "skipped", not as a failure.
                lock (_itemLock) { _itemHasMedia = true; _itemAlready = true; }
            }

            if (data.Contains("Destination:", StringComparison.OrdinalIgnoreCase) || 
                data.Contains("Writing video", StringComparison.OrdinalIgnoreCase) || 
                data.Contains("has already been downloaded", StringComparison.OrdinalIgnoreCase))
            {
                var fileTrackMatch = FileTrackerRegex.Match(data);
                var alreadyDownloadedMatch = AlreadyDownloadedRegex.Match(data);

                if ((fileTrackMatch.Success || alreadyDownloadedMatch.Success) && 
                    !data.Contains("[Merger]") && !data.Contains("[ExtractAudio]"))
                {
                    string path = fileTrackMatch.Success ? fileTrackMatch.Groups[1].Value.Trim() : alreadyDownloadedMatch.Groups[1].Value.Trim();
                    
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        string ext = Path.GetExtension(path)?.ToLower() ?? "";
                        bool isSidecar = ext is ".vtt" or ".srt" or ".ass" or ".ttml" or ".lrc" or ".sbv" or ".ssa" or ".sub"
                                             or ".webp" or ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tiff" or ".json";

                        bool isNewFile;
                        if (alreadyDownloadedMatch.Success && !fileTrackMatch.Success)
                        {
                            // FIX: a file that was ALREADY on disk before this task started is not ours to clean up. It used to be put in
                            // TrackedFiles, so cancelling (or finishing) a bulk job could delete the user's earlier downloads.
                            isNewFile = !task.SettledFiles.Contains(path);
                            if (isNewFile) task.SettledFiles.Add(path);
                            task.FileSize = "";
                        }
                        else
                        {
                            isNewFile = !task.TrackedFiles.Contains(path);
                            if (isNewFile) 
                            {
                                task.TrackedFiles.Add(path);
                                task.FileSize = ""; 
                            }
                        }

                        if (!isSidecar)
                        {
                            lock (_itemLock)
                            {
                                _itemHasMedia = true;
                                if (alreadyDownloadedMatch.Success && !fileTrackMatch.Success) _itemAlready = true;
                                else _freshMedia = true;
                            }
                        }
                        
                        task.OutputPath = path;
                        
                        string cleanTitle = Path.GetFileNameWithoutExtension(path) ?? "Unknown";
                        
                        cleanTitle = Regex.Replace(cleanTitle, @"\.(f\w+|en-orig|en|vtt|webp|jpg)$", "", RegexOptions.IgnoreCase);

                        if (!string.IsNullOrEmpty(cleanTitle) && 
                           (task.Title.Contains("Fetching Title") || task.Title == "Unknown"))
                        {
                            task.Title = cleanTitle;
                            if (!isSidecar) _lastGoodTitle = cleanTitle;
                            OnInfoExtracted?.Invoke(this, new ExtractedInfoEventArgs(task.Id, task.Title, task.OutputPath));
                        }

                        if (alreadyDownloadedMatch.Success) 
                        {
                            task.Progress = 100.0;
                            oldProgress = task.Progress;
                        }

                        if (isNewFile)
                        {
                            if (ext is ".vtt" or ".srt" or ".ass" or ".ttml" or ".lrc" or ".sbv" or ".ssa" or ".sub")
                            {
                                _filePhases[path] = "Downloading Subtitles...";
                            }
                            else if (ext is ".webp" or ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tiff")
                            {
                                _filePhases[path] = "Downloading Thumbnail...";
                            }
                            else if (ext is ".m4a" or ".mp3" or ".ogg" or ".wav" or ".aac" or ".flac" or ".opus" or ".wma" || task.Resolution == "Audio")
                            {
                                _filePhases[path] = "Downloading Audio...";
                            }
                            else
                            {
                                if (!_hasStartedVideoMedia) 
                                {
                                    _filePhases[path] = "Downloading Video...";
                                    _hasStartedVideoMedia = true; 
                                }
                                else 
                                {
                                    _filePhases[path] = "Downloading Audio...";
                                }
                            }
                        }
                        
                        if (alreadyDownloadedMatch.Success && !fileTrackMatch.Success)
                        {
                            task.CurrentPhase = "Already downloaded";
                            task.IsIndeterminate = false;
                        }
                        else if (_filePhases.TryGetValue(path, out var phase))
                        {
                            task.CurrentPhase = phase;
                        }
                        
                        needsUiUpdate = true;
                    }
                }
            }

            if (!_extractionComplete && (data.Contains("[youtube]", StringComparison.OrdinalIgnoreCase) || data.Contains("[info]", StringComparison.OrdinalIgnoreCase)))
            {
                if (task.CurrentPhase == "Starting...")
                {
                    _extractionComplete = true;
                    task.CurrentPhase = "Extracting Info...";
                    if (task.Status != DownloadStatus.Downloading)
                    {
                        task.Status = DownloadStatus.Downloading;
                    }
                    needsUiUpdate = true;
                }
            }

            if (data.Contains("[SponsorBlock]", StringComparison.OrdinalIgnoreCase))
            {
                task.CurrentPhase = "Removing Sponsors...";
                task.IsIndeterminate = true;
                needsUiUpdate = true;
            }
            else if (data.Contains("[ExtractAudio]", StringComparison.OrdinalIgnoreCase))
            {
                task.CurrentPhase = "Converting Audio...";
                task.IsIndeterminate = true;
                needsUiUpdate = true;

                if (data.Contains("Destination:", StringComparison.OrdinalIgnoreCase))
                {
                    var destMatch = FileTrackerRegex.Match(data);
                    if (destMatch.Success) 
                    {
                        string path = destMatch.Groups[1].Value.Trim();
                        task.OutputPath = path;
                        if (!task.TrackedFiles.Contains(path)) task.TrackedFiles.Add(path);
                    }
                }
            }
            else if (data.Contains("[Metadata]", StringComparison.OrdinalIgnoreCase) || data.Contains("[EmbedThumbnail]", StringComparison.OrdinalIgnoreCase))
            {
                task.CurrentPhase = data.Contains("[EmbedThumbnail]", StringComparison.OrdinalIgnoreCase) ? "Embedding Thumbnail..." : "Adding Metadata...";
                task.IsIndeterminate = true;
                needsUiUpdate = true;
            }
            else if (data.Contains("[Merger]", StringComparison.OrdinalIgnoreCase) || data.Contains("[MoveFiles]", StringComparison.OrdinalIgnoreCase) || data.Contains("[Fixup", StringComparison.OrdinalIgnoreCase) || data.Contains("Fixing MPEG-TS", StringComparison.OrdinalIgnoreCase))
            {
                task.CurrentPhase = "Merging & Finalizing...";
                task.IsIndeterminate = true;
                task.Speed = "";
                task.Eta = "";
                needsUiUpdate = true;

                // Without this, OutputPath keeps pointing at a per-stream file (Title.f251.webm) that yt-dlp deletes after the
                // merge, so "Open folder" cannot select the video and "Play" falls back to just opening the folder.
                var mergeMatch = MergerOutputRegex.Match(data);
                if (mergeMatch.Success)
                {
                    string mergedPath = mergeMatch.Groups[1].Value.Trim();
                    task.OutputPath = mergedPath;
                    if (!task.TrackedFiles.Contains(mergedPath)) task.TrackedFiles.Add(mergedPath);
                }

                if (data.Contains("Destination:", StringComparison.OrdinalIgnoreCase))
                {
                    var destMatch = FileTrackerRegex.Match(data);
                    if (destMatch.Success) 
                    {
                        string path = destMatch.Groups[1].Value.Trim();
                        task.OutputPath = path;
                        if (!task.TrackedFiles.Contains(path)) task.TrackedFiles.Add(path);
                    }
                }
            }

            if (data.Contains("[download]", StringComparison.OrdinalIgnoreCase) && data.Contains("%"))
            {
                task.IsIndeterminate = false; 
                
                if (task.Status != DownloadStatus.Downloading)
                {
                    task.Status = DownloadStatus.Downloading;
                    needsUiUpdate = true;
                }

                var sizeMatch = SizeRegex.Match(data);
                if (sizeMatch.Success) 
                { 
                    string parsedSize = sizeMatch.Groups[1].Value.Replace(" ", "");
                    if (task.FileSize != parsedSize)
                    {
                        task.FileSize = parsedSize; 
                        needsUiUpdate = true; 
                    }
                }

                var percentMatch = PercentRegex.Match(data);
                if (percentMatch.Success)
                {
                    if (double.TryParse(percentMatch.Groups[1].Value, out var percent))
                    {
                        task.Progress = Math.Min(percent, 100.0);
                    }
                    else if (data.Contains("100%")) 
                    {
                        task.Progress = 100.0;
                    }
                    
                    if (task.Progress != oldProgress) needsUiUpdate = true;
                }

                var speedMatch = SpeedRegex.Match(data);
                if (speedMatch.Success) { task.Speed = speedMatch.Groups[1].Value; needsUiUpdate = true; }

                var etaMatch = EtaRegex.Match(data);
                if (etaMatch.Success) { task.Eta = etaMatch.Groups[1].Value; needsUiUpdate = true; }
            }
            else if (data.Contains("[download]", StringComparison.OrdinalIgnoreCase))
            {
                // Live / unknown-length downloads: "[download]  12.50MiB at  2.50MiB/s (00:00:05) (frag 14)" - no percentage.
                var liveMatch = LiveProgressRegex.Match(data);
                if (liveMatch.Success)
                {
                    task.IsIndeterminate = true;
                    if (task.Status != DownloadStatus.Downloading) task.Status = DownloadStatus.Downloading;
                    task.FileSize = liveMatch.Groups["size"].Value.Replace(" ", "").TrimStart('~');
                    task.Speed = liveMatch.Groups["speed"].Value.Replace(" ", "");
                    task.Eta = liveMatch.Groups["elapsed"].Value;   // elapsed recording time, shown as "<time> elapsed"
                    needsUiUpdate = true;
                }
            }

            if (needsUiUpdate)
            {
                var now = DateTime.Now;
                if ((now - _lastUiUpdate).TotalMilliseconds >= 100 || task.Progress >= 100.0)
                {
                    _lastUiUpdate = now;
                    OnProgressUpdate?.Invoke(this, new ProgressEventArgs(task.Id, task.Progress, task.Speed, task.Eta));
                }
            }
        }

        private void HandleError(string? data, DownloadTask task)
        {
            if (string.IsNullOrWhiteSpace(data)) return;
            try
            {
                // ffmpeg progress ("frame= ... size= ... time= ... speed=") arrives on stderr when yt-dlp delegates the download to
                // ffmpeg (crop sections, live streams). Without this the app showed no status at all for those downloads.
                var stats = FfmpegStatsRegex.Match(data);
                if (stats.Success)
                {
                    HandleFfmpegStats(data, stats, task);
                    return;
                }

                // One "Opening 'https://...'" line per HLS segment/playlist refresh: pure noise.
                if (FfmpegOpeningRegex.IsMatch(data)) return;

                // Other ffmpeg diagnostics (stream dumps, "Packet corrupt", "skipping N segments ahead"...): keep for the saved
                // log file, but keep them out of the live log view and out of the "last errors" shown on failure.
                if (FfmpegPrefixRegex.IsMatch(data) || FfmpegBlockRegex.IsMatch(data))
                {
                    task.AppendLogQuiet($"[stderr] {data}");
                    if (data.Contains("error", StringComparison.OrdinalIgnoreCase) || data.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        _errorBuffer.AppendLine(data);
                    }
                    return;
                }

                _errorBuffer.AppendLine(data);
                task.AppendLog($"[stderr] {data}");

                if (data.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_itemLock)
                    {
                        _itemError = true;
                        if (data.Contains("DRM", StringComparison.OrdinalIgnoreCase)) _itemDrm = true;
                    }
                    _lastErrorLine = data.Trim();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[ENGINE ERROR] Failed to record yt-dlp stderr line: {ex.Message}");
            }
        }

        private void HandleFfmpegStats(string data, Match m, DownloadTask task)
        {
            bool isFinal = data.Contains("Lsize=", StringComparison.Ordinal);
            var now = DateTime.Now;

            // ffmpeg prints ~2 stats lines per second. Only the first, the last and one every ~30s go to the log; the UI is refreshed
            // at most 4 times per second.
            bool writeToLog = !_loggedFfmpegStart || isFinal || (now - _lastStatsLog).TotalSeconds >= 30;
            if (writeToLog)
            {
                task.AppendLog($"[ffmpeg] {data.Trim()}");
                _loggedFfmpegStart = true;
                _lastStatsLog = now;
            }
            else if ((now - _lastUiUpdate).TotalMilliseconds < 250)
            {
                return;
            }

            double seconds = 0;
            double.TryParse(m.Groups["h"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var h);
            double.TryParse(m.Groups["m"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var min);
            double.TryParse(m.Groups["s"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec);
            seconds = h * 3600 + min * 60 + sec;

            double speedX = 0;
            if (m.Groups["speed"].Success)
            {
                double.TryParse(m.Groups["speed"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out speedX);
            }

            if (m.Groups["size"].Success)
            {
                task.FileSize = FormatFfmpegSize(m.Groups["size"].Value, m.Groups["unit"].Value);
            }

            if (task.Status != DownloadStatus.Downloading) task.Status = DownloadStatus.Downloading;

            if (_sectionSeconds > 0)
            {
                // Known length (crop): real percentage + ETA from ffmpeg's own speed factor.
                task.IsIndeterminate = false;
                task.CurrentPhase = task.CurrentPhase.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase) ? task.CurrentPhase : "Downloading Section...";
                task.Progress = isFinal ? 100.0 : Math.Min(99.9, seconds / _sectionSeconds * 100.0);
                task.Speed = speedX > 0 ? $"{speedX:0.##}x speed" : "";
                task.Eta = !isFinal && speedX > 0.001 ? FormatDuration(Math.Max(0, (_sectionSeconds - seconds) / speedX)) : "";
            }
            else
            {
                // Open-ended (live stream): no total exists, so show how much has been recorded so far.
                task.IsIndeterminate = true;
                task.CurrentPhase = "Downloading Stream...";
                task.Speed = "";
                task.Eta = FormatDuration(seconds);
            }

            _lastUiUpdate = now;
            OnProgressUpdate?.Invoke(this, new ProgressEventArgs(task.Id, task.Progress, task.Speed, task.Eta));
        }

        private static string FormatFfmpegSize(string value, string unit)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return "";
            string u = unit.Replace("i", "").ToUpperInvariant();   // KB / MB / GB / TB / B
            double bytes = u switch
            {
                "KB" => v * 1024,
                "MB" => v * 1024 * 1024,
                "GB" => v * 1024 * 1024 * 1024,
                "TB" => v * 1024.0 * 1024 * 1024 * 1024,
                _ => v
            };
            if (bytes >= 1024.0 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):0.00}GiB";
            if (bytes >= 1024.0 * 1024) return $"{bytes / (1024.0 * 1024):0.00}MiB";
            if (bytes >= 1024.0) return $"{bytes / 1024.0:0}KiB";
            return $"{bytes:0}B";
        }

        private static string FormatDuration(double seconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
                : $"{ts.Minutes}:{ts.Seconds:00}";
        }

        /// <summary>"mm:ss", "hh:mm:ss" or plain seconds -> seconds. Unparseable input (e.g. "inf") -> 0.</summary>
        private static double ParseTimeToSeconds(string text)
        {
            double total = 0;
            foreach (var part in text.Split(':'))
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0;
                total = total * 60 + v;
            }
            return total;
        }

        private string BuildPlaylistInfo()
        {
            string info = $"Item {_itemIndex}/{_bulkTotal}";
            int saved = _savedItems + _skippedItems;
            if (_failedItems > 0) info += $" ({saved} saved, {_failedItems} failed)";
            return info;
        }

        private string BuildResultNote()
        {
            var parts = new List<string>();
            if (_savedItems > 0) parts.Add($"{_savedItems} saved");
            if (_skippedItems > 0) parts.Add($"{_skippedItems} already existed");
            if (_failedItems > 0)
            {
                string failed = $"{_failedItems} failed";
                if (_drmItems > 0) failed += _drmItems == _failedItems ? " (DRM-protected)" : $" ({_drmItems} DRM-protected)";
                parts.Add(failed);
            }
            return parts.Count == 0 ? "nothing downloaded" : string.Join(", ", parts);
        }

        /// <summary>
        /// Closes the books on the item that just ended (a bulk job's per-item boundary, or the end of the process): classifies it as
        /// saved / already existed / failed and "settles" its files so that cancelling later can never delete them.
        /// </summary>
        private void FinishCurrentItem(DownloadTask task, bool settleFiles = true)
        {
            lock (_itemLock)
            {
                if (_itemIndex <= 0) return;

                if (_itemError) 
                {
                    _failedItems++;
                    if (_itemDrm) _drmItems++;
                }
                else if (_itemAlready) _skippedItems++;
                else if (_itemHasMedia) _savedItems++;

                _itemHasMedia = _itemAlready = _itemError = _itemDrm = false;
            }

            // At the very end of the job the last item's leftovers (thumbnail, per-format intermediates) are still handed to the normal
            // "job finished" cleanup, exactly like a single download; only earlier items are settled.
            if (settleFiles) task.SettleCurrentItem();
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
                    dir = Environment.ExpandEnvironmentVariables(dir);
                    if (Directory.Exists(dir)) return dir;
                }
            }

            match = PathTemplateRegex.Match(command);
            if (match.Success)
            {
                var dir = Environment.ExpandEnvironmentVariables(match.Groups[1].Value);
                if (Directory.Exists(dir)) return dir;
            }

            string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloadsPath)) return downloadsPath;
            
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private void SaveLogsToDisk(DownloadTask task)
        {
            string saveDir = string.Empty;
            
            if (!string.IsNullOrWhiteSpace(task.OutputPath))
            {
                saveDir = Path.GetDirectoryName(task.OutputPath) ?? "";
            }

            if (string.IsNullOrEmpty(saveDir))
            {
                saveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }

            string logDir = Path.Combine(saveDir, "YTDLP-Video-logs");
            if (!Directory.Exists(logDir))
            {
                try { Directory.CreateDirectory(logDir); } catch { return; }
            }

            try
            {
                string baseFileName = Path.GetFileNameWithoutExtension(task.FileName);
                if (string.IsNullOrWhiteSpace(baseFileName)) baseFileName = Path.GetFileNameWithoutExtension(task.OutputPath);
                if (string.IsNullOrWhiteSpace(baseFileName) || baseFileName == "Unknown") baseFileName = task.Title;
                
                if (string.IsNullOrWhiteSpace(baseFileName) || baseFileName.Contains("Fetching")) 
                {
                    baseFileName = $"Failed_Download_{task.Id.ToString().Substring(0, 8)}";
                }

                string sanitizedTitle = Regex.Replace(baseFileName, @"[^\w\-\.]", "_");
                string logFileName = $"{sanitizedTitle}.log";
                string finalLogPath = Path.Combine(logDir, logFileName);

                File.WriteAllText(finalLogPath, task.FullLogText, Encoding.UTF8);
                task.AppendLog($"[INFO] Full logs saved to: {finalLogPath}");
                task.LogFileSaved = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save merged log to disk: {ex.Message}");
            }
        }

        public void Cancel()
        {
            _disposed = true;
            try
            {
                _cts.Cancel();
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
            }
            catch { }
        }

        private void CleanupProcess()
        {
            try
            {
                if (_process != null)
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                    _process.Dispose();
                    _process = null;
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Cancel();
            CleanupProcess();
            _cts.Dispose();
        }
    }

    public class ProgressEventArgs : EventArgs
    {
        public Guid TaskId { get; }
        public double Percent { get; }
        public string Speed { get; }
        public string Eta { get; }

        public ProgressEventArgs(Guid taskId, double percent, string speed, string eta)
        {
            TaskId = taskId;
            Percent = percent;
            Speed = speed;
            Eta = eta;
        }
    }

    public class CompleteEventArgs : EventArgs
    {
        public Guid TaskId { get; }
        public string FilePath { get; }
        public string Title { get; }

        public CompleteEventArgs(Guid taskId, string filePath, string title)
        {
            TaskId = taskId;
            FilePath = filePath;
            Title = title;
        }
    }

    public class DownloadErrorEventArgs : EventArgs
    {
        public Guid TaskId { get; }
        public string ErrorMessage { get; }

        public DownloadErrorEventArgs(Guid taskId, string errorMessage)
        {
            TaskId = taskId;
            ErrorMessage = errorMessage;
        }
    }

    public class ExtractedInfoEventArgs : EventArgs
    {
        public Guid TaskId { get; }
        public string Title { get; }
        public string OutputPath { get; }

        public ExtractedInfoEventArgs(Guid taskId, string title, string outputPath)
        {
            TaskId = taskId;
            Title = title;
            OutputPath = outputPath;
        }
    }
}
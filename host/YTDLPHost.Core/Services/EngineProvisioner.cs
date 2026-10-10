using System.Diagnostics;
using System.IO.Compression;

namespace YTDLPHost.Services
{
    /// <summary>Steps reported while missing engine binaries are installed (the UI maps them to text).</summary>
    public enum EngineStage
    {
        DownloadingYtDlp,
        DownloadingFfmpeg,
        ExtractingFfmpeg,
        DownloadingDeno,
        ExtractingDeno
    }

    /// <summary>
    /// What to fetch and what the files are called. Today only the Windows set exists; Phase 3 adds a
    /// per-OS/CPU catalog (with checksums) behind this same shape.
    /// </summary>
    public sealed record EngineAssets(
        string YtDlpFile, string FfmpegFile, string FfprobeFile, string DenoFile,
        string YtDlpUrl, string FfmpegZipUrl, string DenoZipUrl)
    {
        public static EngineAssets Windows { get; } = new(
            "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe",
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe",
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip",
            "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip");
    }

    /// <summary>
    /// Finds, downloads and updates yt-dlp, ffmpeg and deno. Extracted unchanged from MainViewModel
    /// (Phase 1.5a); it has no UI dependencies, so it is unit-tested with a fake HTTP handler.
    /// </summary>
    public sealed class EngineProvisioner
    {
        private readonly IAppPaths _paths;
        private readonly HttpClient _http;
        private readonly EngineAssets _assets;
        private readonly TimeSpan _retryDelay;

        public EngineProvisioner(IAppPaths paths, HttpClient http, EngineAssets? assets = null, TimeSpan? retryDelay = null)
        {
            _paths = paths;
            _http = http;
            _assets = assets ?? EngineAssets.Windows;
            _retryDelay = retryDelay ?? TimeSpan.FromSeconds(3);
        }

        public string EngineDir => _paths.EngineDir;
        public string YtDlpPath => Path.Combine(EngineDir, _assets.YtDlpFile);
        public string FfmpegPath => Path.Combine(EngineDir, _assets.FfmpegFile);
        public string DenoPath => Path.Combine(EngineDir, _assets.DenoFile);

        /// <summary>Creates the engine folder if needed, then reports whether all three tools are present.</summary>
        public bool AllPresent()
        {
            if (!Directory.Exists(EngineDir)) Directory.CreateDirectory(EngineDir);
            return File.Exists(YtDlpPath) && File.Exists(FfmpegPath) && File.Exists(DenoPath);
        }

        /// <summary>Downloads whichever of the three tools is missing. Throws if the network fails after retries.</summary>
        public async Task InstallMissingAsync(Action<EngineStage>? onStage = null)
        {
            string engineDir = EngineDir;
            if (!Directory.Exists(engineDir)) Directory.CreateDirectory(engineDir);

            if (!File.Exists(YtDlpPath))
            {
                AppLogger.Log("[DEPENDENCIES] Downloading yt-dlp binary.");
                onStage?.Invoke(EngineStage.DownloadingYtDlp);

                var ytdlpBytes = await DownloadFileWithRetryAsync(_assets.YtDlpUrl);
                await File.WriteAllBytesAsync(YtDlpPath, ytdlpBytes);

                try { File.Delete(YtDlpPath + ":Zone.Identifier"); } catch { }
            }

            if (!File.Exists(FfmpegPath))
            {
                AppLogger.Log("[DEPENDENCIES] Downloading FFmpeg build archive.");
                onStage?.Invoke(EngineStage.DownloadingFfmpeg);

                string zipPath = Path.Combine(_paths.TempDir, "ffmpeg.zip");
                string extractPath = Path.Combine(_paths.TempDir, "ffmpeg_ext");

                var ffmpegBytes = await DownloadFileWithRetryAsync(_assets.FfmpegZipUrl);
                await File.WriteAllBytesAsync(zipPath, ffmpegBytes);

                AppLogger.Log("[DEPENDENCIES] Extracting FFmpeg archive contents.");
                onStage?.Invoke(EngineStage.ExtractingFfmpeg);

                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                ZipFile.ExtractToDirectory(zipPath, extractPath);

                foreach (var file in Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.Equals(_assets.FfmpegFile, StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals(_assets.FfprobeFile, StringComparison.OrdinalIgnoreCase))
                    {
                        string destPath = Path.Combine(engineDir, fileName);
                        File.Copy(file, destPath, true);
                        try { File.Delete(destPath + ":Zone.Identifier"); } catch { }
                    }
                }

                File.Delete(zipPath);
                Directory.Delete(extractPath, true);
            }

            if (!File.Exists(DenoPath))
            {
                AppLogger.Log("[DEPENDENCIES] Downloading Deno JS engine for EJS puzzle bypass.");
                onStage?.Invoke(EngineStage.DownloadingDeno);

                string denoZipPath = Path.Combine(_paths.TempDir, "deno.zip");
                string denoExtractPath = Path.Combine(_paths.TempDir, "deno_ext");

                var denoBytes = await DownloadFileWithRetryAsync(_assets.DenoZipUrl);
                await File.WriteAllBytesAsync(denoZipPath, denoBytes);

                AppLogger.Log("[DEPENDENCIES] Extracting Deno archive contents.");
                onStage?.Invoke(EngineStage.ExtractingDeno);

                if (Directory.Exists(denoExtractPath)) Directory.Delete(denoExtractPath, true);
                ZipFile.ExtractToDirectory(denoZipPath, denoExtractPath);

                string extractedDeno = Path.Combine(denoExtractPath, _assets.DenoFile);
                if (File.Exists(extractedDeno))
                {
                    File.Copy(extractedDeno, DenoPath, true);
                    try { File.Delete(DenoPath + ":Zone.Identifier"); } catch { }
                }

                File.Delete(denoZipPath);
                Directory.Delete(denoExtractPath, true);
            }
        }

        /// <summary>Fire-and-forget "yt-dlp -U" so the engine stays current without blocking startup.</summary>
        public void StartBackgroundUpdate() => _ = Task.Run(UpdateYtDlp);

        private async Task<byte[]> DownloadFileWithRetryAsync(string url, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await _http.GetByteArrayAsync(url);
                }
                catch (Exception) when (i < maxRetries - 1)
                {
                    AppLogger.Log($"[DEPENDENCIES] Network drop detected on dependency download. Retrying ({i + 1}/{maxRetries})...");
                    await Task.Delay(_retryDelay);
                }
            }
            return await _http.GetByteArrayAsync(url);
        }

        private void UpdateYtDlp()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = YtDlpPath,
                    Arguments = "-U",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = EngineDir
                };

                psi.Environment.Remove("WT_SESSION");
                psi.Environment.Remove("WT_PROFILE_ID");

                using var proc = Process.Start(psi);
                if (proc != null)
                {
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
    }
}

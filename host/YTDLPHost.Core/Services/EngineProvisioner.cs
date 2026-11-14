using System.Diagnostics;
using System.IO.Compression;
using YTDLPHost.Models;

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
    /// What to fetch and what the files are called. The checksum URLs are optional so that tests can use plain fake assets; every real set in
    /// <see cref="EngineCatalog"/> has them for every download. An empty <paramref name="FfmpegZipUrl"/> means "use the ffmpeg of the system".
    /// </summary>
    public sealed record EngineAssets(
        string YtDlpFile, string FfmpegFile, string FfprobeFile, string DenoFile,
        string YtDlpUrl, string FfmpegZipUrl, string DenoZipUrl,
        string? YtDlpSumsUrl = null, string? FfmpegSumsUrl = null, string? DenoSumsUrl = null)
    {
        public bool FfmpegFromSystem => string.IsNullOrEmpty(FfmpegZipUrl);

        public static EngineAssets Windows { get; } = new(
            "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe",
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe",
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip",
            "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip",
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS",
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/checksums.sha256",
            "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip.sha256sum");
    }

    /// <summary>
    /// Finds, downloads and updates yt-dlp, ffmpeg and deno. Extracted unchanged from MainViewModel
    /// (Phase 1.5a); it has no UI dependencies, so it is unit-tested with a fake HTTP handler.
    /// </summary>
    public sealed class EngineProvisioner : IEngineProvisioner
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

                var ytdlpBytes = await DownloadVerifiedAsync(_assets.YtDlpUrl, _assets.YtDlpSumsUrl);
                await WriteAtomicAsync(YtDlpPath, ytdlpBytes);

                try { File.Delete(YtDlpPath + ":Zone.Identifier"); } catch { }
            }

            if (!File.Exists(FfmpegPath))
            {
                AppLogger.Log("[DEPENDENCIES] Downloading FFmpeg build archive.");
                onStage?.Invoke(EngineStage.DownloadingFfmpeg);

                string zipPath = Path.Combine(_paths.TempDir, "ffmpeg.zip");
                string extractPath = Path.Combine(_paths.TempDir, "ffmpeg_ext");

                var ffmpegBytes = await DownloadVerifiedAsync(_assets.FfmpegZipUrl, _assets.FfmpegSumsUrl);
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
                        CopyAtomically(file, destPath);
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

                var denoBytes = await DownloadVerifiedAsync(_assets.DenoZipUrl, _assets.DenoSumsUrl);
                await File.WriteAllBytesAsync(denoZipPath, denoBytes);

                AppLogger.Log("[DEPENDENCIES] Extracting Deno archive contents.");
                onStage?.Invoke(EngineStage.ExtractingDeno);

                if (Directory.Exists(denoExtractPath)) Directory.Delete(denoExtractPath, true);
                ZipFile.ExtractToDirectory(denoZipPath, denoExtractPath);

                string extractedDeno = Path.Combine(denoExtractPath, _assets.DenoFile);
                if (File.Exists(extractedDeno))
                {
                    CopyAtomically(extractedDeno, DenoPath);
                    try { File.Delete(DenoPath + ":Zone.Identifier"); } catch { }
                }

                File.Delete(denoZipPath);
                Directory.Delete(denoExtractPath, true);
            }
        }

        /// <summary>
        /// Set by the app: starts the engine update checks (at launch, then daily). Called once the engine is installed and the queue may run.
        /// Nothing happens while it is unset (tests).
        /// </summary>
        public Action? BackgroundUpdateStarter { get; set; }

        public void StartBackgroundUpdate() => BackgroundUpdateStarter?.Invoke();

        /// <summary>The updater for this engine folder; it reads the release channel from the saved settings on every run.</summary>
        public EngineUpdater CreateUpdater() =>
            new EngineUpdater(YtDlpPath, EngineDir, () => NormalizeUpdateChannel(AppSettings.Load().UpdateChannel));

        /// <summary>
        /// Downloads a file and checks it against the checksum file its project publishes with the release. A mismatch is tried once more (the "latest" release
        /// of BtbN can change between the two downloads); after that, or when the checksum file has no entry for the file, nothing is installed
        /// (<see cref="EngineVerificationException"/>). Without a checksum URL (test assets only) the file is taken as it is.
        /// </summary>
        private async Task<byte[]> DownloadVerifiedAsync(string url, string? sumsUrl)
        {
            if (string.IsNullOrEmpty(sumsUrl))
            {
                AppLogger.Log($"[DEPENDENCIES] No checksum source for {url}; the download is not verified.");
                return await DownloadFileWithRetryAsync(url);
            }

            string asset = Path.GetFileName(Uri.UnescapeDataString(new Uri(url).AbsolutePath));
            bool perFileSums = sumsUrl.EndsWith(".sha256sum", StringComparison.OrdinalIgnoreCase);
            string problem = "";
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                byte[] bytes = await DownloadFileWithRetryAsync(url);
                string sums = await DownloadTextWithRetryAsync(sumsUrl);

                string? expected = Sha256Sums.FindHash(sums, asset, perFileSums);
                if (expected == null) throw new EngineVerificationException($"The published checksums ({sumsUrl}) have no entry for {asset}.");

                string actual = Sha256Hex.Of(bytes);
                if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)) return bytes;

                problem = $"Checksum mismatch for {asset}: published {expected}, downloaded {actual}.";
                AppLogger.Log("[DEPENDENCIES] " + problem + (attempt == 1 ? " Trying once more." : ""));
            }
            throw new EngineVerificationException(problem);
        }

        /// <summary>Writes next to the target and renames, so a crash never leaves a half-written tool that File.Exists would accept.</summary>
        private static async Task WriteAtomicAsync(string path, byte[] bytes)
        {
            string temp = path + ".download";
            try
            {
                await File.WriteAllBytesAsync(temp, bytes);
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private static void CopyAtomically(string source, string dest)
        {
            string temp = dest + ".download";
            try
            {
                File.Copy(source, temp, true);
                File.Move(temp, dest, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private Task<byte[]> DownloadFileWithRetryAsync(string url) => WithRetryAsync(() => _http.GetByteArrayAsync(url));

        private Task<string> DownloadTextWithRetryAsync(string url) => WithRetryAsync(() => _http.GetStringAsync(url));

        private async Task<T> WithRetryAsync<T>(Func<Task<T>> action, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await action();
                }
                catch (Exception) when (i < maxRetries - 1)
                {
                    AppLogger.Log($"[DEPENDENCIES] Network drop detected on dependency download. Retrying ({i + 1}/{maxRetries})...");
                    await Task.Delay(_retryDelay);
                }
            }
            return await action();
        }

        /// <summary>
        /// yt-dlp's own release channels. Anything unrecognised (or empty) falls back to nightly, where YouTube fixes land first.
        /// </summary>
        internal static string NormalizeUpdateChannel(string? raw)
        {
            var channel = (raw ?? "").Trim().ToLowerInvariant();
            return channel is "stable" or "nightly" or "master" ? channel : "nightly";
        }
    }
}

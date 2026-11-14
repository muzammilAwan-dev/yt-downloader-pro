using System.Runtime.InteropServices;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Phase 3, step 3.5: which engine files to fetch on which OS and CPU. File names checked in November 2026 against the projects' own pages:
    /// yt-dlp (yt-dlp.exe, yt-dlp_arm64.exe, yt-dlp_macos [universal], yt-dlp_linux, yt-dlp_linux_aarch64, with SHA2-256SUMS), Deno (deno-&lt;target&gt;.zip with a
    /// per-file .sha256sum) and BtbN FFmpeg-Builds ("latest" release: ffmpeg-master-latest-win64-gpl.zip, ...-winarm64-gpl.zip, with checksums.sha256).
    /// All three float on "latest" on purpose: yt-dlp must be current to work, and BtbN keeps only the last 14 daily builds, so a hard-coded hash or version
    /// would break within weeks. What protects the download is the checksum file of the same release (see EngineProvisioner).
    /// Not covered yet: ffmpeg on macOS (BtbN has no macOS build) and Linux (BtbN ships .tar.xz only and .NET has no xz reader), so those use the system ffmpeg
    /// (<see cref="EngineAssets.FfmpegFromSystem"/>). Other CPUs (32-bit x86, armv7) get null.
    /// </summary>
    public static class EngineCatalog
    {
        private const string YtDlp = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";
        private const string YtDlpSums = YtDlp + "SHA2-256SUMS";
        private const string Deno = "https://github.com/denoland/deno/releases/latest/download/";
        private const string Btbn = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/";
        private const string BtbnSums = Btbn + "checksums.sha256";

        public static EngineAssets? ForCurrentPlatform() => For(AppPathsFactory.CurrentOs(), RuntimeInformation.OSArchitecture);

        public static EngineAssets? For(HostOs os, Architecture arch) => (os, arch) switch
        {
            (HostOs.Windows, Architecture.X64) => EngineAssets.Windows,
            (HostOs.Windows, Architecture.Arm64) => new EngineAssets(
                "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe",
                YtDlp + "yt-dlp_arm64.exe", Btbn + "ffmpeg-master-latest-winarm64-gpl.zip", Deno + "deno-aarch64-pc-windows-msvc.zip",
                YtDlpSums, BtbnSums, Deno + "deno-aarch64-pc-windows-msvc.zip.sha256sum"),
            (HostOs.MacOS, Architecture.X64) => Unix(YtDlp + "yt-dlp_macos", "deno-x86_64-apple-darwin.zip"),
            (HostOs.MacOS, Architecture.Arm64) => Unix(YtDlp + "yt-dlp_macos", "deno-aarch64-apple-darwin.zip"),
            (HostOs.Linux, Architecture.X64) => Unix(YtDlp + "yt-dlp_linux", "deno-x86_64-unknown-linux-gnu.zip"),
            (HostOs.Linux, Architecture.Arm64) => Unix(YtDlp + "yt-dlp_linux_aarch64", "deno-aarch64-unknown-linux-gnu.zip"),
            _ => null
        };

        private static EngineAssets Unix(string ytDlpUrl, string denoZip) => new EngineAssets(
            "yt-dlp", "ffmpeg", "ffprobe", "deno",
            ytDlpUrl, "", Deno + denoZip,
            YtDlpSums, null, Deno + denoZip + ".sha256sum");
    }
}

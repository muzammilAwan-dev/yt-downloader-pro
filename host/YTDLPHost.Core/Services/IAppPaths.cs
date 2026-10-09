namespace YTDLPHost.Services
{
    /// <summary>
    /// Every location the app reads or writes, in one place. Core code and the host ask this
    /// interface instead of building paths from Environment.SpecialFolder, so Phase 3 can supply
    /// per-OS layouts (LocalAppData / ~/Library/Application Support / XDG) without touching callers.
    /// </summary>
    public interface IAppPaths
    {
        /// <summary>The user's home / profile folder.</summary>
        string HomeDir { get; }

        /// <summary>Default save location when a command does not name one.</summary>
        string DownloadsDir { get; }

        /// <summary>Where yt-dlp, ffmpeg and deno live.</summary>
        string EngineDir { get; }

        /// <summary>settings.json and history.json.</summary>
        string DataDir { get; }

        /// <summary>host_debug.log and crash_log.txt.</summary>
        string LogsDir { get; }

        /// <summary>Handoff payloads waiting for the running instance to pick up.</summary>
        string PayloadsDir { get; }

        /// <summary>Scratch space (cookie files, archive extraction).</summary>
        string TempDir { get; }
    }

    /// <summary>
    /// Process-wide access point until the host gets real dependency injection (Phase 1.5).
    /// Set <see cref="Current"/> once at startup, before the first AppLogger or settings call.
    /// </summary>
    public static class AppPaths
    {
        public static IAppPaths Current { get; set; } = new LegacyAppPaths();
    }

    /// <summary>
    /// Today's layout, unchanged, so existing Windows installs keep their settings, history, engine
    /// binaries and queued payloads exactly where they are. Replaced per-OS in Phase 3.
    /// </summary>
    public sealed class LegacyAppPaths : IAppPaths
    {
        private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public string HomeDir => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public string DownloadsDir => Path.Combine(HomeDir, "Downloads");
        public string EngineDir => Path.Combine(LocalAppData, "YTDownloaderProEngine");
        public string DataDir => EngineDir;
        public string LogsDir => EngineDir;
        public string PayloadsDir => Path.Combine(LocalAppData, "YT Downloader Pro", "Payloads");
        public string TempDir => Path.GetTempPath();
    }
}

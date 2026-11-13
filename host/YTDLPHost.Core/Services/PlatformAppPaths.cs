namespace YTDLPHost.Services
{
    public enum HostOs { Windows, MacOS, Linux }

    /// <summary>
    /// Phase 3, step 3.1: picks the folder layout of the operating system. Windows keeps <see cref="LegacyAppPaths"/> exactly as it is, so existing
    /// installs find their settings, history and engine files. macOS and Linux are new platforms, so they have nothing to migrate.
    /// Pure code (the OS, the environment and the home folder are parameters), so every layout is tested on every OS. The Native Messaging shim
    /// links this file and IAppPaths.cs as source (it must not reference Core), so keep it free of anything but System and System.IO.
    /// </summary>
    public static class AppPathsFactory
    {
        public static HostOs CurrentOs() =>
            OperatingSystem.IsWindows() ? HostOs.Windows : OperatingSystem.IsMacOS() ? HostOs.MacOS : HostOs.Linux;

        public static IAppPaths CreateDefault() =>
            Create(CurrentOs(), Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.GetTempPath());

        public static IAppPaths Create(HostOs os, Func<string, string?> getEnv, string home, string temp) => os switch
        {
            HostOs.Windows => new LegacyAppPaths(),
            HostOs.MacOS => new MacAppPaths(home, temp),
            _ => new XdgAppPaths(home, getEnv, temp)   // every other Unix follows the XDG layout
        };
    }

    /// <summary>~/Library/Application Support/YT Downloader Pro for data, engine and payloads; ~/Library/Logs/YT Downloader Pro for logs.</summary>
    public sealed class MacAppPaths : IAppPaths
    {
        private readonly string _temp;

        public MacAppPaths(string home, string temp)
        {
            HomeDir = home;
            _temp = temp;
        }

        private string Base => Path.Combine(HomeDir, "Library", "Application Support", "YT Downloader Pro");

        public string HomeDir { get; }
        public string DownloadsDir => Path.Combine(HomeDir, "Downloads");
        public string EngineDir => Path.Combine(Base, "engine");
        public string DataDir => Base;
        public string LogsDir => Path.Combine(HomeDir, "Library", "Logs", "YT Downloader Pro");
        public string PayloadsDir => Path.Combine(Base, "Payloads");
        public string TempDir => _temp;
    }

    /// <summary>
    /// The XDG Base Directory layout: data and engine under XDG_DATA_HOME, settings under XDG_CONFIG_HOME, logs and queued payloads under XDG_STATE_HOME.
    /// Per the spec a variable that is empty or not an absolute path is ignored.
    /// </summary>
    public sealed class XdgAppPaths : IAppPaths
    {
        private const string AppFolder = "yt-downloader-pro";
        private readonly Func<string, string?> _env;
        private readonly string _temp;

        public XdgAppPaths(string home, Func<string, string?> getEnv, string temp)
        {
            HomeDir = home;
            _env = getEnv;
            _temp = temp;
        }

        private string Base(string variable, params string[] fallback)
        {
            string? value = _env(variable);
            string root = !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value) ? value : Path.Combine(HomeDir, Path.Combine(fallback));
            return Path.Combine(root, AppFolder);
        }

        public string HomeDir { get; }
        public string DownloadsDir => Path.Combine(HomeDir, "Downloads");
        public string DataDir => Base("XDG_DATA_HOME", ".local", "share");
        public string ConfigDir => Base("XDG_CONFIG_HOME", ".config");
        public string EngineDir => Path.Combine(DataDir, "engine");
        public string LogsDir => Path.Combine(Base("XDG_STATE_HOME", ".local", "state"), "logs");
        public string PayloadsDir => Path.Combine(Base("XDG_STATE_HOME", ".local", "state"), "payloads");
        public string TempDir => _temp;
    }
}

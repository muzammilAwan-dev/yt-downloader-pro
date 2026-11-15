namespace YTDLPHost.Services
{
    /// <summary>The engine needs a tool that the app does not download (ffmpeg on macOS and Linux) and the computer does not have it.</summary>
    public sealed class EngineMissingToolException : Exception
    {
        public string Tool { get; }

        public EngineMissingToolException(string tool, string message) : base(message) => Tool = tool;
    }

    /// <summary>What the search for system tools needs from the machine; injectable so the search is tested on any OS.</summary>
    public sealed record ToolSearch(HostOs Os, string? PathVariable, Func<string, bool> FileExists)
    {
        public static ToolSearch ForThisComputer() =>
            new(AppPathsFactory.CurrentOs(), Environment.GetEnvironmentVariable("PATH"), File.Exists);
    }

    /// <summary>
    /// Phase 3, step 3.5b: finds a program the app does not install itself (ffmpeg and ffprobe on macOS and Linux). Looks through PATH, then through the
    /// usual install folders: a macOS app started from Finder does not get the shell's PATH (recalled, not verified here), so Homebrew's
    /// folders are not on it. Relative PATH entries are ignored on purpose: a tool must never be taken from the current folder.
    /// </summary>
    public static class SystemTools
    {
        /// <summary>Full path of the first match, or null.</summary>
        public static string? Find(string name, ToolSearch search)
        {
            string? dir = FindFolder(name, search);
            return dir == null ? null : Join(dir, name, search.Os);
        }

        /// <summary>The folder that holds the first match, or null.</summary>
        public static string? FindFolder(string name, ToolSearch search)
        {
            foreach (var dir in SearchFolders(search))
                if (search.FileExists(Join(dir, name, search.Os))) return dir;
            return null;
        }

        /// <summary>Folder + file name with the separator of the given OS ("\\" on Windows, where the file gets ".exe").</summary>
        public static string Join(string dir, string name, HostOs os)
        {
            char separator = os == HostOs.Windows ? '\\' : '/';
            string file = os == HostOs.Windows && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name + ".exe" : name;
            return dir.TrimEnd(separator) + separator + file;
        }

        private static IEnumerable<string> SearchFolders(ToolSearch search)
        {
            char listSeparator = search.Os == HostOs.Windows ? ';' : ':';
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var raw in (search.PathVariable ?? "").Split(listSeparator))
            {
                string dir = raw.Trim().Trim('"');
                if (IsAbsolute(dir, search.Os) && seen.Add(dir)) yield return dir;
            }

            foreach (var dir in WellKnownFolders(search.Os))
                if (seen.Add(dir)) yield return dir;
        }

        private static string[] WellKnownFolders(HostOs os) => os switch
        {
            HostOs.MacOS => new[] { "/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin", "/usr/bin" },
            HostOs.Linux => new[] { "/usr/local/bin", "/usr/bin", "/bin", "/snap/bin" },
            _ => Array.Empty<string>()
        };

        private static bool IsAbsolute(string dir, HostOs os)
        {
            if (dir.Length == 0) return false;
            if (os != HostOs.Windows) return dir[0] == '/';
            return dir.Length >= 3 && char.IsLetter(dir[0]) && dir[1] == ':' && (dir[2] == '\\' || dir[2] == '/');
        }
    }

    /// <summary>
    /// Where the three tools really are, for one set of <see cref="EngineAssets"/>: yt-dlp and deno always in the engine folder, ffmpeg either there
    /// (downloaded) or on the system. Resolved again for every download, so an ffmpeg installed while the app runs is picked up.
    /// </summary>
    public sealed class EngineTools
    {
        public string YtDlpPath { get; }
        public string DenoPath { get; }

        /// <summary>Full path of ffmpeg, or empty when it is a system tool that was not found.</summary>
        public string FfmpegPath { get; }

        /// <summary>The value for yt-dlp's --ffmpeg-location: the ffmpeg file for the downloaded one (as before), the folder for a system one (so ffprobe is looked up next to it). Empty = not found.</summary>
        public string FfmpegLocation { get; }

        public bool FfmpegFromSystem { get; }
        public bool FfmpegFound => FfmpegPath.Length > 0;

        private EngineTools(string ytDlp, string deno, string ffmpeg, string location, bool fromSystem)
        {
            YtDlpPath = ytDlp; DenoPath = deno; FfmpegPath = ffmpeg; FfmpegLocation = location; FfmpegFromSystem = fromSystem;
        }

        public static EngineTools Resolve(IAppPaths paths, EngineAssets assets, ToolSearch search)
        {
            string engineDir = paths.EngineDir;
            string ytDlp = Path.Combine(engineDir, assets.YtDlpFile);
            string deno = Path.Combine(engineDir, assets.DenoFile);

            if (!assets.FfmpegFromSystem)
            {
                string ffmpeg = Path.Combine(engineDir, assets.FfmpegFile);
                return new EngineTools(ytDlp, deno, ffmpeg, ffmpeg, fromSystem: false);
            }

            // yt-dlp looks for ffprobe in the folder it is given, so a system ffmpeg only counts when ffprobe sits next to it.
            string? dir = SystemTools.FindFolder(assets.FfmpegFile, search);
            if (dir != null && search.FileExists(SystemTools.Join(dir, assets.FfprobeFile, search.Os)))
                return new EngineTools(ytDlp, deno, SystemTools.Join(dir, assets.FfmpegFile, search.Os), dir, fromSystem: true);
            return new EngineTools(ytDlp, deno, "", "", fromSystem: true);
        }
    }
}

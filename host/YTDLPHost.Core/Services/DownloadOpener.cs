using YTDLPHost.Models;

namespace YTDLPHost.Services
{
    /// <summary>
    /// The "open folder" and "play" buttons: decides what to open (the finished file, else its folder, else the
    /// save folder named in the command) and asks <see cref="IShellActions"/> to do it. The decision logic moved
    /// unchanged from MainViewModel (Phase 1.5d); only the Process.Start calls went behind the interface.
    /// </summary>
    public sealed class DownloadOpener
    {
        private readonly IShellActions _shell;

        public DownloadOpener(IShellActions shell)
        {
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        }

        /// <summary>Test seam: how the save folder is found from a command. Defaults to <see cref="ExtractSaveDirectory"/>.</summary>
        internal Func<string, string> SaveDirectoryFromCommand { get; set; } = ExtractSaveDirectory;

        /// <summary>Selects the finished file in the file manager; falls back to its folder, then to the save folder.</summary>
        public void ShowInFolder(DownloadTask task)
        {
            var path = task.OutputPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                _shell.RevealFile(path);
                return;
            }

            if (!string.IsNullOrEmpty(path))
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    _shell.OpenFolder(dir);
                    return;
                }
            }

            var saveDir = SaveDirectoryFromCommand(task.Command);
            if (Directory.Exists(saveDir)) _shell.OpenFolder(saveDir);
        }

        /// <summary>Opens the finished file with its default app; if it is gone, behaves like <see cref="ShowInFolder"/>.</summary>
        public void Play(DownloadTask task)
        {
            var path = task.OutputPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                _shell.OpenFile(path);
                return;
            }

            ShowInFolder(task);
        }

        internal static string ExtractSaveDirectory(string command) => ExtractSaveDirectory(command, AppPaths.Current.HomeDir);

        /// <summary>
        /// The folder named by the command's -o template ("~" = home, environment variables expanded, created if missing), else the home folder.
        /// Step 3.6: backslashes are only swapped in on Windows (this was the last Windows-only part; the runner's second copy went in 1.6).
        /// </summary>
        internal static string ExtractSaveDirectory(string command, string homeDir)
        {
            var match = CommandInfo.OutputTemplateRegex.Match(command);
            if (match.Success)
            {
                var template = match.Groups[1].Value;
                var dir = Path.GetDirectoryName(template);
                if (!string.IsNullOrEmpty(dir))
                {
                    if (OperatingSystem.IsWindows()) dir = dir.Replace("/", "\\");
                    dir = Environment.ExpandEnvironmentVariables(dir);
                    if (dir.StartsWith("~")) dir = Path.Combine(homeDir, dir.Substring(1).TrimStart('\\', '/'));
                    if (!Directory.Exists(dir)) try { Directory.CreateDirectory(dir); } catch { }
                    if (Directory.Exists(dir)) return dir;
                }
            }
            return homeDir;
        }
    }
}

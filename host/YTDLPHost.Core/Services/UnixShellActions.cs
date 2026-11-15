using System.Diagnostics;

namespace YTDLPHost.Services
{
    /// <summary>One program to start and its arguments (never a shell string, so a file name cannot be read as a command).</summary>
    public sealed record ShellCommand(string FileName, IReadOnlyList<string> Arguments);

    /// <summary>
    /// Phase 3, step 3.6: which programs show a file or folder on macOS and Linux. Each method returns the attempts in order; the first one that works wins.
    /// Windows has its own implementation in the WPF project (Explorer), so it gets no commands here.
    /// Linux "show this file": the freedesktop FileManager1 D-Bus call selects the file in the user's file manager (recalled from the freedesktop specification,
    /// not tried here); when there is no dbus-send or no such service, the folder is opened with xdg-open instead (no selection).
    /// </summary>
    public static class UnixShellCommands
    {
        public static IReadOnlyList<ShellCommand> Reveal(HostOs os, string path) => os switch
        {
            HostOs.MacOS => new[] { Command("open", "-R", path) },
            HostOs.Linux => new[]
            {
                Command("dbus-send", "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "--type=method_call",
                        "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems", "array:string:" + FileUri(path), "string:"),
                Command("xdg-open", ParentFolder(path))
            },
            _ => Array.Empty<ShellCommand>()
        };

        public static IReadOnlyList<ShellCommand> OpenFolder(HostOs os, string path) => OpenWithDefault(os, path);

        public static IReadOnlyList<ShellCommand> OpenFile(HostOs os, string path) => OpenWithDefault(os, path);

        private static IReadOnlyList<ShellCommand> OpenWithDefault(HostOs os, string path) => os switch
        {
            HostOs.MacOS => new[] { Command("open", path) },
            HostOs.Linux => new[] { Command("xdg-open", path) },
            _ => Array.Empty<ShellCommand>()
        };

        private static ShellCommand Command(string fileName, params string[] arguments) => new(fileName, arguments);

        /// <summary>"file:///home/u/My%20Videos/a.mp4": every segment escaped, built by hand so it is the same on every OS.</summary>
        internal static string FileUri(string path) =>
            "file://" + string.Join("/", path.Split('/').Select(Uri.EscapeDataString));

        internal static string ParentFolder(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash <= 0 ? "/" : path.Substring(0, slash);
        }
    }

    /// <summary>
    /// Opens files and folders on macOS and Linux. Like every <see cref="IShellActions"/> it never throws: a failure is logged.
    /// Only absolute paths are accepted (a path that starts with "-" must never reach a program as an option).
    /// </summary>
    public sealed class UnixShellActions : IShellActions
    {
        private readonly HostOs _os;
        private readonly Func<ShellCommand, bool> _run;

        /// <param name="run">Starts a command and says whether it worked. Default: really start it. Tests pass a fake.</param>
        public UnixShellActions(HostOs os, Func<ShellCommand, bool>? run = null)
        {
            _os = os;
            _run = run ?? ShellCommandRunner.StartAndCheck;
        }

        public void RevealFile(string path) => Try(UnixShellCommands.Reveal(_os, path), path);

        public void OpenFolder(string path) => Try(UnixShellCommands.OpenFolder(_os, path), path);

        public void OpenFile(string path) => Try(UnixShellCommands.OpenFile(_os, path), path);

        private void Try(IReadOnlyList<ShellCommand> attempts, string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '/')
            {
                AppLogger.Log($"[SHELL ERROR] Refused to open '{path}': not an absolute path.");
                return;
            }

            foreach (var command in attempts)
            {
                try
                {
                    if (_run(command)) return;
                    AppLogger.Log($"[SHELL] '{command.FileName}' did not work, trying the next way.");
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[SHELL] '{command.FileName}' could not be started: {ex.Message}");
                }
            }
            AppLogger.Log($"[SHELL ERROR] Could not open '{path}'.");
        }
    }
}

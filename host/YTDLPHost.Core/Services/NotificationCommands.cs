using System.Text;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Phase 4, step 4.2: the program that shows a desktop notification on each OS, as plain data (like <see cref="UnixShellCommands"/>), so it is tested on every system.
    /// Why no notification library: the cross-platform one for Avalonia (DesktopNotifications 1.3.x) was last released in October 2024, its Avalonia helper
    /// package still depends on Avalonia 0.10, its macOS part uses the API Apple deprecated in 10.14 (the author asks for help there), and its Windows part
    /// needs a Windows-only target framework (checked on nuget.org and the project's GitHub page, 2026-11-15).
    /// Limits of this approach: no click callback (<see cref="INotifier.Activated"/> is never raised); on macOS the notice comes from "Script Editor", on Windows from
    /// "Windows PowerShell" (a toast needs an app identity; a proper one needs the installed/signed app of Phase 5).
    /// </summary>
    public static class NotificationCommands
    {
        public const string AppName = "YT Downloader Pro";
        private const int MaxTitle = 80;
        private const int MaxBody = 250;

        // The Application User Model ID of Windows PowerShell 5.1: toasts are only shown for a registered ID.
        private const string PowerShellAppId = @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe";

        public static IReadOnlyList<ShellCommand> Build(HostOs os, string title, string body)
        {
            title = Clean(title, MaxTitle);
            body = Clean(body, MaxBody);

            return os switch
            {
                // "--" ends the options, so a file name that starts with "-" is not read as one.
                HostOs.Linux => new[] { new ShellCommand("notify-send", new[] { "--app-name=" + AppName, "--", title, body }) },
                HostOs.MacOS => new[] { new ShellCommand("osascript", new[] { "-e", $"display notification \"{AppleScriptText(body)}\" with title \"{AppleScriptText(title)}\"" }) },
                HostOs.Windows => new[] { new ShellCommand("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", EncodedToastScript(title, body) }) },
                _ => Array.Empty<ShellCommand>()
            };
        }

        /// <summary>One line, no control characters, cut to a sane length (a file name can be very long).</summary>
        internal static string Clean(string text, int max)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text) sb.Append(char.IsControl(c) ? ' ' : c);
            string clean = sb.ToString().Trim();
            return clean.Length <= max ? clean : clean.Substring(0, max - 3) + "...";
        }

        internal static string AppleScriptText(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        internal static string ToastScript(string title, string body)
        {
            string xml = "<toast><visual><binding template=\"ToastGeneric\"><text>" + XmlText(title) + "</text><text>" + XmlText(body) + "</text></binding></visual></toast>";
            return string.Join("\n",
                "$ErrorActionPreference = 'Stop'",
                "$null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]",
                "$null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]",
                "$xml = New-Object Windows.Data.Xml.Dom.XmlDocument",
                "$xml.LoadXml('" + xml.Replace("'", "''") + "')",
                "$toast = New-Object Windows.UI.Notifications.ToastNotification $xml",
                "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('" + PowerShellAppId + "').Show($toast)");
        }

        // -EncodedCommand (UTF-16 in base64) means no quoting problem can reach the command line, whatever the file name contains.
        private static string EncodedToastScript(string title, string body) =>
            Convert.ToBase64String(Encoding.Unicode.GetBytes(ToastScript(title, body)));

        private static string XmlText(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
    }

    /// <summary>
    /// Shows desktop notifications by starting the OS's own tool (notify-send, osascript, PowerShell). Never throws, never blocks the caller:
    /// the program is started on a background thread. See <see cref="NotificationCommands"/> for the limits.
    /// </summary>
    public sealed class SystemNotifier : INotifier
    {
        private readonly HostOs _os;
        private readonly Func<ShellCommand, bool> _run;
        private readonly Action<Action> _background;

        /// <param name="run">Starts a command and says whether it worked. Default: really start it. Tests pass a fake.</param>
        /// <param name="background">Runs the work off the caller's thread. Default: the thread pool. Tests run it at once.</param>
        public SystemNotifier(HostOs os, Func<ShellCommand, bool>? run = null, Action<Action>? background = null)
        {
            _os = os;
            _run = run ?? ShellCommandRunner.StartAndCheck;
            _background = background ?? (work => _ = Task.Run(work));
        }

        /// <summary>Never raised: a notification started through a command-line tool cannot report a click.</summary>
        public event EventHandler? Activated { add { } remove { } }

        public void NotifyDownloadComplete(string fileName) =>
            Post("Download Complete", $"\"{fileName}\" finished downloading.");

        public void NotifyDownloadStarted(int count) =>
            Post("Download started", count == 1 ? "A download from your browser was added." : $"{count} downloads from your browser were added.");

        private void Post(string title, string body) => _background(() => Send(title, body));

        /// <summary>Shows one notification now. True when a program was started for it.</summary>
        public bool Send(string title, string body)
        {
            foreach (var command in NotificationCommands.Build(_os, title, body))
            {
                try
                {
                    if (_run(command)) return true;
                    AppLogger.Log($"[NOTIFY] '{command.FileName}' did not work.");
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[NOTIFY] '{command.FileName}' could not be started: {ex.Message}");
                }
            }
            return false;
        }
    }
}

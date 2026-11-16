using System.Text;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Phase 4, step 4.2: the program that shows a desktop notification on each OS, as plain data (like <see cref="UnixShellCommands"/>), so it is tested on every system.
    /// Why no notification library: the cross-platform one for Avalonia (DesktopNotifications 1.3.x) was last released in October 2024, its Avalonia helper
    /// package still depends on Avalonia 0.10, its macOS part uses the API Apple deprecated in 10.14 (the author asks for help there), and its Windows part
    /// needs a Windows-only target framework (checked on nuget.org and the project's GitHub page, 2026-11-15).
    /// Identity: Windows shows a toast only for a registered app identity (AUMID). The script registers ours (name and icon) under HKCU, as the BurntToast
    /// PowerShell module does for unpackaged apps, so the toast shows "YT Downloader Pro" and the logo. Linux gets the name and the icon through notify-send.
    /// Limits: no click callback (<see cref="INotifier.Activated"/> is never raised); on macOS the notice comes from "Script Editor" (a proper identity needs
    /// the signed app bundle of Phase 5).
    /// </summary>
    public static class NotificationCommands
    {
        public const string AppName = "YT Downloader Pro";
        private const int MaxTitle = 80;
        private const int MaxBody = 250;

        /// <summary>Our Application User Model ID: the registry key name under HKCU\Software\Classes\AppUserModelId.</summary>
        public const string AppId = "YTDownloaderPro.Avalonia";

        /// <param name="iconPath">Full path of a PNG with the logo, or null. Linux passes it to notify-send, Windows registers it with the identity.</param>
        public static IReadOnlyList<ShellCommand> Build(HostOs os, string title, string body, string? iconPath = null)
        {
            title = Clean(title, MaxTitle);
            body = Clean(body, MaxBody);

            return os switch
            {
                // "--" ends the options, so a file name that starts with "-" is not read as one.
                HostOs.Linux => new[] { new ShellCommand("notify-send", LinuxArguments(title, body, iconPath)) },
                HostOs.MacOS => new[] { new ShellCommand("osascript", new[] { "-e", $"display notification \"{AppleScriptText(body)}\" with title \"{AppleScriptText(title)}\"" }) },
                HostOs.Windows => new[] { new ShellCommand("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", EncodedToastScript(title, body, iconPath) }) },
                _ => Array.Empty<ShellCommand>()
            };
        }

        private static string[] LinuxArguments(string title, string body, string? iconPath)
        {
            var args = new List<string> { "--app-name=" + AppName };
            if (!string.IsNullOrEmpty(iconPath)) args.Add("--icon=" + iconPath);
            args.Add("--");   // ends the options
            args.Add(title);
            args.Add(body);
            return args.ToArray();
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

        internal static string ToastScript(string title, string body, string? iconPath = null)
        {
            string xml = "<toast><visual><binding template=\"ToastGeneric\"><text>" + XmlText(title) + "</text><text>" + XmlText(body) + "</text></binding></visual></toast>";
            // Register our identity (name, and icon when there is one) so Windows shows them on the toast. Safe to repeat.
            var register = new List<string>
            {
                "$key = 'HKCU:\\Software\\Classes\\AppUserModelId\\" + AppId + "'",
                "if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }",
                "New-ItemProperty -Path $key -Name DisplayName -Value '" + PsText(AppName) + "' -PropertyType String -Force | Out-Null"
            };
            if (!string.IsNullOrEmpty(iconPath))
                register.Add("New-ItemProperty -Path $key -Name IconUri -Value '" + PsText(iconPath) + "' -PropertyType String -Force | Out-Null");

            return string.Join("\n",
                "$ErrorActionPreference = 'Stop'",
                string.Join("\n", register),
                "$null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]",
                "$null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]",
                "$xml = New-Object Windows.Data.Xml.Dom.XmlDocument",
                "$xml.LoadXml('" + PsText(xml) + "')",
                "$toast = New-Object Windows.UI.Notifications.ToastNotification $xml",
                "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('" + AppId + "').Show($toast)");
        }

        // -EncodedCommand (UTF-16 in base64) means no quoting problem can reach the command line, whatever the file name contains.
        private static string EncodedToastScript(string title, string body, string? iconPath) =>
            Convert.ToBase64String(Encoding.Unicode.GetBytes(ToastScript(title, body, iconPath)));

        // Inside a PowerShell single-quoted string only the quote itself needs care.
        private static string PsText(string text) => text.Replace("'", "''");

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
        private readonly string? _iconPath;

        /// <param name="iconPath">Full path of the logo PNG shown on the notification (Windows and Linux), or null.</param>
        /// <param name="run">Starts a command and says whether it worked. Default: really start it. Tests pass a fake.</param>
        /// <param name="background">Runs the work off the caller's thread. Default: the thread pool. Tests run it at once.</param>
        public SystemNotifier(HostOs os, Func<ShellCommand, bool>? run = null, Action<Action>? background = null, string? iconPath = null)
        {
            _os = os;
            _iconPath = iconPath;
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
            foreach (var command in NotificationCommands.Build(_os, title, body, _iconPath))
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

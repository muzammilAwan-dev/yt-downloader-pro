using System.Diagnostics;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

/// <summary>
/// Step 4.5: whether this desktop has a system tray (see <see cref="TrayPolicy"/>), found once at first use and logged.
/// Where it has none, the window minimises instead of hiding. Setting the environment variable YTDLP_FORCE_TRAY to 1 or 0 overrides the
/// detection (for a desktop that is detected wrongly, and for testing).
/// </summary>
public static class TraySupport
{
    private static readonly Lazy<bool> Detected = new(Detect);

    public static bool CanHideToTray => Detected.Value;

    private static bool Detect()
    {
        var os = AppPathsFactory.CurrentOs();
        bool wayland = TrayPolicy.IsWayland(Environment.GetEnvironmentVariable);

        string? forced = Environment.GetEnvironmentVariable("YTDLP_FORCE_TRAY");
        if (forced == "1" || forced == "0")
        {
            AppLogger.Log($"[TRAY] Forced by YTDLP_FORCE_TRAY={forced}.");
            return forced == "1";
        }

        bool? watcher = os == HostOs.Linux ? LinuxStatusNotifierProbe.WatcherPresent() : null;
        bool can = TrayPolicy.CanHideToTray(os, watcher);
        AppLogger.Log($"[TRAY] OS {os}{(wayland ? ", Wayland session" : "")}; StatusNotifier watcher: {(watcher?.ToString() ?? "unknown")}; hide to tray: {can}."
                      + (can ? "" : " The window will minimise instead of hiding."));
        return can;
    }
}

/// <summary>Asks the session bus whether a StatusNotifier watcher exists, with dbus-send or gdbus (one of them is on every desktop Linux).</summary>
internal static class LinuxStatusNotifierProbe
{
    public static bool? WatcherPresent()
    {
        bool? result = null;
        foreach (string name in TrayPolicy.WatcherNames)
        {
            bool? answer = Ask(name);
            if (answer == true) return true;
            if (answer == false) result = false;
        }
        return result;
    }

    private static bool? Ask(string name)
    {
        var attempts = new (string File, string[] Args)[]
        {
            ("dbus-send", new[] { "--session", "--print-reply", "--dest=org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus.NameHasOwner", "string:" + name }),
            ("gdbus", new[] { "call", "--session", "--dest", "org.freedesktop.DBus", "--object-path", "/org/freedesktop/DBus", "--method", "org.freedesktop.DBus.NameHasOwner", name })
        };

        foreach (var (file, args) in attempts)
        {
            try
            {
                var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (string arg in args) info.ArgumentList.Add(arg);
                using var process = Process.Start(info);
                if (process == null) continue;

                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(3000))
                {
                    try { process.Kill(); } catch { /* already gone */ }
                    continue;
                }
                bool? parsed = TrayPolicy.ParseNameHasOwner(output.GetAwaiter().GetResult());
                if (parsed != null) return parsed;
            }
            catch
            {
                // the tool is not installed: try the next one
            }
        }
        return null;
    }
}

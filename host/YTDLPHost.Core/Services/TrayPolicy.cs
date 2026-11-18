namespace YTDLPHost.Services
{
    /// <summary>
    /// Step 4.5: can the window hide into the system tray on this machine? Windows and macOS always have one. On Linux the tray is the
    /// StatusNotifier protocol: KDE, Xfce and Cinnamon provide it, GNOME only with an AppIndicator extension. When nobody owns the watcher's name on the
    /// session bus, a hidden window could not be brought back except by starting the app again, so the app minimises instead.
    /// Pure code (the answer of the bus and the environment are parameters), tested on every OS.
    /// </summary>
    public static class TrayPolicy
    {
        /// <summary>The names a StatusNotifier watcher can own (the first one is what KDE, GNOME's extension and the others use today).</summary>
        public static readonly string[] WatcherNames = { "org.kde.StatusNotifierWatcher", "org.freedesktop.StatusNotifierWatcher" };

        /// <summary>
        /// Reads the reply of <c>dbus-send --print-reply ... NameHasOwner</c> ("boolean true") or of <c>gdbus call ... NameHasOwner</c> ("(true,)").
        /// Returns null for anything else (an error text, nothing): the caller then does not know.
        /// </summary>
        public static bool? ParseNameHasOwner(string? output)
        {
            if (string.IsNullOrWhiteSpace(output)) return null;
            string text = output.Trim();
            if (text.Contains("boolean true", StringComparison.Ordinal) || text == "(true,)") return true;
            if (text.Contains("boolean false", StringComparison.Ordinal) || text == "(false,)") return false;
            return null;
        }

        /// <summary>
        /// True when a window may be hidden into the tray. Linux needs a watcher on the bus; "unknown" (no D-Bus tool, no session bus) counts as no,
        /// because minimising a window that could have been hidden costs nothing, while hiding one that cannot be brought back loses it.
        /// </summary>
        public static bool CanHideToTray(HostOs os, bool? watcherPresent) => os != HostOs.Linux || watcherPresent == true;

        /// <summary>Wayland sessions do not let an app raise its own window; used for the log line only.</summary>
        public static bool IsWayland(Func<string, string?> getEnvironmentVariable) =>
            string.Equals(getEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(getEnvironmentVariable("WAYLAND_DISPLAY"));
    }
}

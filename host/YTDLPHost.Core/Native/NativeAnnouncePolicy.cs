namespace YTDLPHost.Native
{
    /// <summary>What the app does so the user notices that a download from the browser just started.</summary>
    public enum NativeAnnouncement
    {
        /// <summary>Nothing: the window is already on screen and shows the new row, or nothing new was added.</summary>
        None,

        /// <summary>Bring the main window to the front.</summary>
        ShowWindow,

        /// <summary>A short "download started" notification (the window is hidden in the tray).</summary>
        Notify
    }

    /// <summary>
    /// Browser downloads no longer make the app pop up on their own (the old ytdlp:// link did, as a side effect of Chrome opening the
    /// app). The browser page says "sent to YT Downloader Pro"; this decides what the app itself does. Pure, so it is tested.
    /// </summary>
    public static class NativeAnnouncePolicy
    {
        public static NativeAnnouncement Decide(int newDownloads, bool windowVisible, bool showWindowSetting)
        {
            if (newDownloads <= 0) return NativeAnnouncement.None; // a repeat of a running download adds nothing
            if (showWindowSetting) return windowVisible ? NativeAnnouncement.None : NativeAnnouncement.ShowWindow;
            return windowVisible ? NativeAnnouncement.None : NativeAnnouncement.Notify;
        }
    }
}

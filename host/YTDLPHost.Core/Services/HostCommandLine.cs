namespace YTDLPHost.Services
{
    public enum HostCommand { None, RegisterNativeHost, UnregisterNativeHost }

    /// <summary>
    /// Phase 3, step 3.4: command line switches of the host that do a job and exit instead of starting the app.
    /// <c>--register-native-host</c> links the browsers to this install (the same thing the installer and the start-up check do; for portable
    /// installs on macOS and Linux), <c>--unregister-native-host</c> removes that link. Anything else, such as a ytdlp:// link, is not a command.
    /// </summary>
    public static class HostCommandLine
    {
        public static HostCommand Parse(IEnumerable<string> args)
        {
            foreach (string arg in args)
            {
                if (arg.Equals("--register-native-host", StringComparison.OrdinalIgnoreCase)) return HostCommand.RegisterNativeHost;
                if (arg.Equals("--unregister-native-host", StringComparison.OrdinalIgnoreCase)) return HostCommand.UnregisterNativeHost;
            }
            return HostCommand.None;
        }
    }
}

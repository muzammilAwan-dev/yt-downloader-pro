using System.Text.RegularExpressions;

namespace YTDLPHost.Services
{
    /// <summary>What the builder needs to know about this machine and the user's settings.</summary>
    /// <param name="FfmpegPath">Full path of ffmpeg.</param>
    /// <param name="DenoPath">Full path of deno, or null when it is not installed.</param>
    /// <param name="SpeedLimit">"0" = unlimited, otherwise a yt-dlp rate such as "2M".</param>
    /// <param name="YoutubePlayerClients">Emergency override from settings.json; "" = let yt-dlp choose.</param>
    /// <param name="CookieFilePath">Netscape cookie file to pass, or null for none.</param>
    public sealed record YtDlpEnvironment(string FfmpegPath, string? DenoPath, string SpeedLimit, string YoutubePlayerClients, string? CookieFilePath);

    /// <summary>The finished invocation: the argument list plus what the runner needs to know about it.</summary>
    public sealed record YtDlpInvocation(
        IReadOnlyList<string> Arguments,
        bool IsYoutube,
        int RequestedHeight,
        string? SectionStart,
        string? SectionEnd,
        IReadOnlyList<string> AppLog,
        IReadOnlyList<string> TaskLog)
    {
        /// <summary>For the task log only: the arguments joined, with the ones that contain spaces in quotes.</summary>
        public string ToDisplayString() => string.Join(" ", Arguments.Select(Quote));

        private static string Quote(string argument) =>
            argument.Length == 0 || argument.IndexOfAny(new[] { ' ', '\t' }) >= 0 ? "\"" + argument + "\"" : argument;
    }

    /// <summary>
    /// Turns a validated command string into the argument list yt-dlp is started with (Phase 1.6). It replaces the string
    /// surgery that used to live in YtDlpRunner (Contains / Replace / += on one long string): the command is tokenized once,
    /// the app's own additions are appended as separate arguments, and the process gets them through
    /// ProcessStartInfo.ArgumentList, so nothing can be re-split or re-quoted on the way. Pure: no file or process access.
    /// </summary>
    public static class YtDlpCommandBuilder
    {
        private static readonly Regex YoutubeUrlRegex = new(@"https?://(?:[\w-]+\.)*(?:youtube\.com|youtu\.be|youtube-nocookie\.com)/", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RequestedHeightRegex = new(@"height<=(\d+)", RegexOptions.Compiled);
        private static readonly Regex PlayerClientListRegex = new(@"^[A-Za-z0-9_,\-]+$", RegexOptions.Compiled);
        private static readonly Regex SectionRangeRegex = new(@"^\*?(?<a>[\d:\.]+)-(?<b>[\d:\.]+)$", RegexOptions.Compiled);

        public static bool IsYoutubeCommand(string command) => YoutubeUrlRegex.IsMatch(command);

        /// <exception cref="FormatException">The command has an unclosed quote.</exception>
        public static YtDlpInvocation Build(string command, YtDlpEnvironment env)
        {
            var args = CommandTokenizer.Tokenize(command.Trim()) ?? throw new FormatException("The command has an unclosed quote.");
            var appLog = new List<string>();
            var taskLog = new List<string>();

            // The executable is started separately; drop the "yt-dlp" the extension puts in front.
            if (args.Count > 0 && (args[0].Equals("yt-dlp", StringComparison.OrdinalIgnoreCase) || args[0].Equals("yt-dlp.exe", StringComparison.OrdinalIgnoreCase)))
                args.RemoveAt(0);

            // A strict "audio only" request fails on some clients; "ba/b" falls back to the best combined format.
            for (int i = 0; i + 1 < args.Count; i++)
            {
                if (args[i] == "-f" && args[i + 1] == "ba")
                {
                    args[i + 1] = "ba/b";
                    appLog.Add("[ENGINE] Upgraded strict Audio request to 'ba/b' fallback for Mobile API compatibility.");
                }
            }

            // Embed the thumbnail in extracted audio, except WAV (cannot hold one). Links are not checked for "wav".
            if (args.Contains("--extract-audio") && !args.Contains("--embed-thumbnail") && !MentionsWav(args))
                args.Add("--embed-thumbnail");

            if (!args.Contains("--ffmpeg-location"))
            {
                args.Add("--ffmpeg-location");
                args.Add(env.FfmpegPath);
            }

            if (!string.IsNullOrEmpty(env.DenoPath) && !args.Contains("--js-runtimes"))
            {
                args.Add("--js-runtimes");
                args.Add($"deno:{env.DenoPath}");
            }

            if (!string.IsNullOrWhiteSpace(env.SpeedLimit) && env.SpeedLimit != "0" && !args.Contains("--limit-rate"))
            {
                args.Add("--limit-rate");
                args.Add(env.SpeedLimit);
            }

            bool isYoutube = args.Any(a => YoutubeUrlRegex.IsMatch(a));
            int requestedHeight = 0;
            var heightMatch = RequestedHeightRegex.Match(string.Join(" ", args));
            if (heightMatch.Success) int.TryParse(heightMatch.Groups[1].Value, out requestedHeight);

            if (isYoutube)
            {
                // --no-warnings hides yt-dlp's explanation when YouTube withholds formats ("... require a GVS PO Token ...").
                // Keep warnings for YouTube so the log says WHY a download came out low quality.
                args.RemoveAll(a => a == "--no-warnings");

                // Do NOT pin player_client here. The upstream default list is retuned in every yt-dlp release; a hard-coded list
                // goes stale and silently caps quality. The settings.json override exists only as a manual emergency hotfix.
                var clients = env.YoutubePlayerClients?.Trim() ?? "";
                if (clients.Length > 0 && PlayerClientListRegex.IsMatch(clients) && !args.Contains("--extractor-args"))
                {
                    args.Add("--extractor-args");
                    args.Add($"youtube:player_client={clients}");
                    taskLog.Add($"[ENGINE] YouTube player_client override from settings: {clients}");
                }
            }

            if (!string.IsNullOrEmpty(env.CookieFilePath) && !args.Contains("--cookies"))
            {
                args.Add("--cookies");
                args.Add(env.CookieFilePath);
            }

            string? sectionStart = null, sectionEnd = null;
            int sectionIndex = args.IndexOf("--download-sections");
            if (sectionIndex >= 0 && sectionIndex + 1 < args.Count)
            {
                var range = SectionRangeRegex.Match(args[sectionIndex + 1]);
                if (range.Success)
                {
                    sectionStart = range.Groups["a"].Value;
                    sectionEnd = range.Groups["b"].Value;
                }
            }

            return new YtDlpInvocation(args, isYoutube, requestedHeight, sectionStart, sectionEnd, appLog, taskLog);
        }

        private static bool MentionsWav(List<string> args) =>
            args.Any(a => !a.StartsWith("http", StringComparison.OrdinalIgnoreCase) && a.Contains("wav", StringComparison.OrdinalIgnoreCase));
    }
}

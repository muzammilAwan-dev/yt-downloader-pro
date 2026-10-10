using System.Text;
using System.Text.RegularExpressions;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Pure helpers that read facts out of a yt-dlp command string (resolution, title hint, a stable
    /// "same video" key) and decode payload parts. Extracted unchanged from MainViewModel (Phase 1.5b).
    /// </summary>
    public static class CommandInfo
    {
        public static readonly Regex OutputTemplateRegex = new(@"-o\s+""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex ResHeightRegex = new(@"height<=?(\d+)", RegexOptions.Compiled);
        private static readonly Regex ResRegex = new(@"(\d+)p", RegexOptions.Compiled);

        /// <summary>Decodes standard or URL-safe base64, with or without padding.</summary>
        public static string DecodeBase64(string input)
        {
            string padded = input.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }

        /// <summary>
        /// A stable key for "is this the same content?" so two downloads of one video never run at once.
        /// YouTube: the canonical 11-char video id, so watch/shorts/embed/youtu.be forms match.
        /// Anything else: the last quoted argument (the target URL), which is stable across calls.
        /// </summary>
        public static string ExtractVideoId(string command)
        {
            var ytMatch = Regex.Match(command, @"(?:v=|youtu\.be/|shorts/|embed/)([\w-]{11})");
            if (ytMatch.Success) return "yt:" + ytMatch.Groups[1].Value;

            var urlMatches = Regex.Matches(command, "\"([^\"]+)\"");
            if (urlMatches.Count > 0) return urlMatches[urlMatches.Count - 1].Groups[1].Value;

            return command; // last resort: still a stable key, just a coarse one
        }

        public static string ExtractResolution(string command)
        {
            if (command.Contains("ba") && (command.Contains("extract-audio") || command.Contains("audio"))) return "Audio";
            var match = ResHeightRegex.Match(command);
            if (match.Success) return match.Groups[1].Value + "p";
            match = ResRegex.Match(command);
            if (match.Success) return match.Groups[1].Value + "p";
            return "";
        }

        public static string ExtractTitleHint(string command)
        {
            var match = OutputTemplateRegex.Match(command);
            if (match.Success)
            {
                string fileName = Path.GetFileNameWithoutExtension(match.Groups[1].Value) ?? "Fetching Title...";
                return fileName.Replace("%(title)s", "Fetching Title...").Replace("%(uploader)s", "Channel");
            }
            return "Fetching Title...";
        }
    }
}

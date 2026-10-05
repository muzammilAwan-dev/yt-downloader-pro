using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Allowlist validator for command lines arriving over the ytdlp:// protocol.
    ///
    /// Why an allowlist: any web page can navigate to a ytdlp:// link, so the payload must be treated as
    /// untrusted input. A blocklist of "bad" flags (--exec, ...) is easy to bypass (--external-downloader,
    /// --ffmpeg-location, --config-locations, unquoted -o, --output=..., etc.), so instead only the flags the
    /// extension actually emits (plus a few harmless extras for custom commands) are accepted, output files
    /// must end in a media extension inside a sane directory, and every positional argument must be an
    /// http(s) URL. Anything else is rejected with a reason that is written to the app log.
    /// </summary>
    public static class CommandValidator
    {
        // Flags that take exactly one value.
        private static readonly HashSet<string> ValueFlags = new(StringComparer.Ordinal)
        {
            "-f", "--format", "-S", "--format-sort", "-N", "--concurrent-fragments", "-o",
            "--merge-output-format", "--audio-format", "--audio-quality", "--download-sections",
            "--sub-langs", "--sleep-subtitles", "--sleep-requests", "--sleep-interval", "--max-sleep-interval",
            "--playlist-items", "--sponsorblock-remove", "--download-archive", "--limit-rate",
            "-R", "--retries", "--fragment-retries", "--socket-timeout"
        };

        // Flags without a value.
        private static readonly HashSet<string> BoolFlags = new(StringComparer.Ordinal)
        {
            "-x", "--extract-audio", "--force-keyframes-at-cuts", "--write-subs", "--write-auto-subs",
            "--embed-subs", "--no-playlist", "--yes-playlist", "--restrict-filenames", "--windows-filenames",
            "--embed-metadata", "--embed-thumbnail", "--write-thumbnail", "--no-warnings", "--progress",
            "--ignore-errors", "-i", "--no-mtime", "--no-overwrites", "--continue"
        };

        private static readonly string[] MediaExtensions =
            { ".mp4", ".mkv", ".webm", ".mp3", ".m4a", ".aac", ".opus", ".ogg", ".flac", ".wav" };

        private static readonly string[] ArchiveExtensions = { ".txt" };

        // Lower-case fragments (backslash separated) that output paths may never contain.
        private static readonly string[] BlockedPathFragments =
        {
            @"\windows\", @"\system32", @"\program files", @"\programdata", @"\start menu", @"\startup",
            @"\appdata\roaming\microsoft", "ytdownloaderproengine"
        };

        private static readonly Regex UrlRegex = new(@"^https?://\S+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DriveRegex = new(@"^[a-z]:\\", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool TryValidate(string command, out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(command)) { reason = "empty command"; return false; }
            if (command.IndexOfAny(new[] { '\n', '\r', '\0' }) >= 0) { reason = "control characters in command"; return false; }
            // A backslash right before a quote changes how Windows splits arguments, which would let the
            // tokenizer below and the real parser disagree. The extension never produces this sequence.
            if (command.Contains("\\\"")) { reason = "escaped quote in command"; return false; }

            var tokens = Tokenize(command);
            if (tokens == null || tokens.Count == 0) { reason = "unbalanced quotes"; return false; }

            var exe = tokens[0];
            if (!exe.Equals("yt-dlp", StringComparison.OrdinalIgnoreCase) &&
                !exe.Equals("yt-dlp.exe", StringComparison.OrdinalIgnoreCase))
            {
                reason = "command must start with yt-dlp";
                return false;
            }

            int urlCount = 0;
            for (int i = 1; i < tokens.Count; i++)
            {
                var arg = tokens[i];

                if (arg.StartsWith("-", StringComparison.Ordinal))
                {
                    if (BoolFlags.Contains(arg)) continue;

                    if (ValueFlags.Contains(arg))
                    {
                        if (i + 1 >= tokens.Count) { reason = $"missing value for {arg}"; return false; }
                        var value = tokens[++i];

                        if (value.StartsWith("--", StringComparison.Ordinal)) { reason = $"invalid value for {arg}"; return false; }

                        if (arg == "-o" && !IsPathAllowed(value, MediaExtensions)) { reason = "output path or file type not allowed"; return false; }
                        if (arg == "--download-archive" && !IsPathAllowed(value, ArchiveExtensions)) { reason = "archive path not allowed"; return false; }
                        continue;
                    }

                    reason = $"option not allowed: {arg}";
                    return false;
                }

                if (UrlRegex.IsMatch(arg)) { urlCount++; continue; }

                reason = "unexpected argument (only http/https links are accepted)";
                return false;
            }

            if (urlCount == 0) { reason = "no link in command"; return false; }
            return true;
        }

        private static List<string>? Tokenize(string command)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            bool hasToken = false;

            foreach (var ch in command)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    hasToken = true;
                }
                else if ((ch == ' ' || ch == '\t') && !inQuotes)
                {
                    if (hasToken) { tokens.Add(current.ToString()); current.Clear(); hasToken = false; }
                }
                else
                {
                    current.Append(ch);
                    hasToken = true;
                }
            }

            if (inQuotes) return null;
            if (hasToken) tokens.Add(current.ToString());
            return tokens;
        }

        private static bool IsPathAllowed(string path, string[] allowedExtensions)
        {
            var normalized = path.Replace('/', '\\').ToLowerInvariant();

            if (Array.IndexOf(normalized.Split('\\'), "..") >= 0) return false;
            if (normalized.StartsWith(@"\\", StringComparison.Ordinal)) return false; // UNC / network paths
            if (!DriveRegex.IsMatch(normalized) && !normalized.StartsWith("~", StringComparison.Ordinal)) return false;

            var withTrailing = normalized + "\\";
            foreach (var fragment in BlockedPathFragments)
            {
                if (withTrailing.Contains(fragment, StringComparison.Ordinal)) return false;
            }

            foreach (var ext in allowedExtensions)
            {
                if (normalized.EndsWith(ext, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}

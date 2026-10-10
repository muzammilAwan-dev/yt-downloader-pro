using System.Text;

namespace YTDLPHost.Services
{
    public enum IngestStatus { Ignored, Blocked, Parsed }

    /// <param name="UrlPayload">The percent-decoded ytdlp:// string as received.</param>
    /// <param name="Command">The decoded and validated yt-dlp command.</param>
    /// <param name="CookiePart">Base64 cookie file, or null.</param>
    /// <param name="UserAgentPart">Base64 User-Agent, or null.</param>
    public sealed record ParsedPayload(string UrlPayload, string Command, string? CookiePart, string? UserAgentPart);

    public sealed record IngestResult(IngestStatus Status, ParsedPayload? Payload = null, string? BlockReason = null);

    public sealed record PreparedCommand(string Command, string? CookieContent, string CookieFilePath);

    /// <summary>
    /// Turns a raw ytdlp:// link (untrusted: any web page can open one) into a validated command.
    /// Stage 1 <see cref="Parse"/> decodes and validates; stage 2 <see cref="Prepare"/> writes the cookie file
    /// and adds the User-Agent. They are separate because the duplicate check sits between them and must
    /// happen before any cookie file is written. Extracted from MainViewModel.ProcessUrl (Phase 1.5b).
    /// </summary>
    public sealed class CommandIngest
    {
        private const int MaxUserAgentLength = 512;

        private readonly IAppPaths _paths;

        public CommandIngest(IAppPaths paths) => _paths = paths;

        /// <summary>Malformed base64 in the command part throws FormatException; the caller logs and drops it.</summary>
        public IngestResult Parse(string rawUrl)
        {
            string url = Uri.UnescapeDataString(rawUrl);

            if (!url.StartsWith("ytdlp://", StringComparison.OrdinalIgnoreCase)) return new IngestResult(IngestStatus.Ignored);

            var payload = url.Substring(8).TrimEnd('/');
            var parts = payload.Split(new[] { "||" }, StringSplitOptions.None);

            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) return new IngestResult(IngestStatus.Ignored);

            var command = CommandInfo.DecodeBase64(parts[0]);
            AppLogger.Log("[QUEUE] New command payload successfully parsed.");

            if (string.IsNullOrWhiteSpace(command)) return new IngestResult(IngestStatus.Ignored);

            if (!CommandValidator.TryValidate(command, out var reason))
            {
                AppLogger.Log($"[SECURITY] Blocked command payload: {reason}");
                return new IngestResult(IngestStatus.Blocked, BlockReason: reason);
            }

            string? cookiePart = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : null;
            string? userAgentPart = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : null;

            return new IngestResult(IngestStatus.Parsed, new ParsedPayload(url, command, cookiePart, userAgentPart));
        }

        /// <summary>Writes the session cookies to a temp file and appends the browser's User-Agent.</summary>
        public PreparedCommand Prepare(ParsedPayload payload)
        {
            string command = payload.Command;
            string? cookieContent = null;
            string? cookieFilePath = null;

            if (payload.CookiePart != null)
            {
                try
                {
                    cookieContent = CommandInfo.DecodeBase64(payload.CookiePart);
                    if (!string.IsNullOrWhiteSpace(cookieContent))
                    {
                        cookieContent = cookieContent.TrimStart('\uFEFF');

                        Directory.CreateDirectory(_paths.TempDir);
                        var cookieFile = Path.Combine(_paths.TempDir, $"ytdlp_cookies_{Guid.NewGuid()}.txt");
                        File.WriteAllText(cookieFile, cookieContent, new UTF8Encoding(false));
                        cookieFilePath = cookieFile;
                        AppLogger.Log("[COOKIES] Session cookies provisioned to local temporary storage.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[COOKIES ERROR] Cookie deserialization failure: {ex.Message}");
                }
            }

            if (payload.UserAgentPart != null)
            {
                try
                {
                    string exactUserAgent = CommandInfo.DecodeBase64(payload.UserAgentPart);
                    if (!string.IsNullOrWhiteSpace(exactUserAgent) && !command.Contains("--user-agent"))
                    {
                        // SECURITY: this value arrives in the third part of the link, AFTER the command was
                        // validated. Appended unchecked, a value like  x" --exec "calc  would end the quoted
                        // argument and add flags the validator never saw. So: strict character check, then the
                        // complete command is validated again; if either fails the User-Agent is simply skipped.
                        string candidate = command + $" --user-agent \"{exactUserAgent}\"";
                        if (IsSafeUserAgent(exactUserAgent) && CommandValidator.TryValidate(candidate, out _))
                        {
                            command = candidate;
                            AppLogger.Log("[COOKIES] Injected native browser User-Agent to match cookies.");
                        }
                        else
                        {
                            AppLogger.Log("[SECURITY] Ignored unsafe User-Agent supplied in payload.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[COOKIES ERROR] User-Agent deserialization failure: {ex.Message}");
                }
            }

            return new PreparedCommand(command, cookieContent, cookieFilePath ?? string.Empty);
        }

        /// <summary>Printable ASCII only, no quote or backslash, bounded length. Real browser UAs fit easily.</summary>
        internal static bool IsSafeUserAgent(string ua)
        {
            if (ua.Length == 0 || ua.Length > MaxUserAgentLength) return false;
            foreach (char c in ua)
            {
                if (c < 0x20 || c > 0x7E || c == '"' || c == '\\') return false;
            }
            return true;
        }
    }
}

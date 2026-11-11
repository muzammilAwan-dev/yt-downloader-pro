using System.Text.RegularExpressions;

namespace YTDLPHost.Services
{
    /// <summary>The validation rules for the settings screen, moved out of MainViewModel (Phase 1.5e) so they can be tested.</summary>
    public static class SettingsRules
    {
        public const int MinConcurrentDownloads = 1;
        public const int MaxConcurrentDownloads = 15;

        // "0" (unlimited) or a number with a unit that yt-dlp understands: 500K, 2.5M, 1G.
        private static readonly Regex SpeedLimitPattern = new(@"^\d+(\.\d+)?[KMG]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // A one-URL request used only to ask the real mapper and command checker "would this folder work for a browser download?".
        private static readonly byte[] FolderProbeRequest = System.Text.Encoding.UTF8.GetBytes(
            "{\"v\":1,\"id\":\"folder-check\",\"type\":\"download\",\"urls\":[\"https://example.com/video\"],\"options\":{\"quality\":\"720\"}}");

        /// <summary>
        /// The folder browser downloads are saved to. Empty is fine (the app's default folder). Otherwise it must be a full path and one the
        /// browser-download code can actually use: the SAME mapper and command checker that run for a real download are asked, so a folder
        /// that passes here can never make every browser download fail later.
        /// </summary>
        public static bool TryNormalizeDownloadFolder(string? raw, out string stored, out string error)
        {
            stored = "";
            error = "";

            string value = (raw ?? "").Trim();
            if (value.Length == 0) return true;

            bool driveLetter = value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/');
            bool absolute = driveLetter || value.StartsWith('/') || value.StartsWith('\\') || value == "~" || value.StartsWith("~/") || value.StartsWith("~\\");
            if (!absolute)
            {
                error = "Please enter a full folder path, for example D:\\Videos.";
                return false;
            }

            var parsed = YTDLPHost.Native.NativeRequestParser.Parse(FolderProbeRequest);
            if (!parsed.Ok || parsed.Request is not YTDLPHost.Native.DownloadRequest probe)
            {
                error = "The folder could not be checked.";
                return false;
            }

            var mapped = YTDLPHost.Native.DownloadRequestMapper.Map(probe, new YTDLPHost.Native.MapperSettings(value));
            if (!mapped.Ok)
            {
                error = "This folder cannot be used for browser downloads: " + mapped.Error;
                return false;
            }

            stored = value;
            return true;
        }

        public static int ClampConcurrentDownloads(int value) =>
            Math.Clamp(value, MinConcurrentDownloads, MaxConcurrentDownloads);

        /// <summary>
        /// Accepts "0" or a number followed by K, M or G (any case, surrounding spaces ignored; null counts as "0").
        /// On success returns the form that is stored and passed to yt-dlp, e.g. " 2.5m " -> "2.5M".
        /// </summary>
        public static bool TryNormalizeSpeedLimit(string? raw, out string normalized)
        {
            string speed = raw?.Trim() ?? "0";
            if (speed != "0" && !SpeedLimitPattern.IsMatch(speed))
            {
                normalized = "";
                return false;
            }

            normalized = speed.Replace(" ", "").ToUpperInvariant();
            return true;
        }
    }
}

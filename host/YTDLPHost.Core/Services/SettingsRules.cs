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

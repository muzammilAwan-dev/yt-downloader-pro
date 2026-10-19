using System.Text.RegularExpressions;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Detects when YouTube withholds the good formats (PO-token / SABR enforcement) and yt-dlp silently falls back to a
    /// legacy combined format. "[info] id: Downloading 1 format(s): 18" is the only trace of that, so each output line is
    /// checked. Moved out of YtDlpRunner in Phase 1.6 so it can be tested.
    /// </summary>
    public sealed class YoutubeQualityGuard
    {
        private static readonly Regex FormatPickRegex = new(@"\[info\]\s+\S+:\s+Downloading\s+\d+\s+format\(s\):\s+(?<ids>\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Dictionary<string, int> LegacyMuxedHeights = new() { ["17"] = 144, ["36"] = 240, ["18"] = 360, ["22"] = 720 };

        private readonly bool _isYoutube;
        private readonly int _requestedHeight;

        public YoutubeQualityGuard(bool isYoutube, int requestedHeight)
        {
            _isYoutube = isYoutube;
            _requestedHeight = requestedHeight;
        }

        /// <summary>A guard that never reports anything (non-YouTube downloads, and the runner before it has started).</summary>
        public static YoutubeQualityGuard Disabled => new(false, 0);

        /// <summary>Set once the guard has tripped, e.g. "only 360p offered by YouTube (wanted 1080p)". Shown next to "Completed".</summary>
        public string Note { get; private set; } = "";

        /// <summary>Looks at one output line. Returns an explanation for the log the first time the guard trips, otherwise null.</summary>
        public string? Observe(string line)
        {
            if (!_isYoutube || _requestedHeight <= 0 || Note.Length > 0) return null;

            var pick = FormatPickRegex.Match(line);
            if (!pick.Success) return null;

            string ids = pick.Groups["ids"].Value;
            if (!LegacyMuxedHeights.TryGetValue(ids, out var gotHeight) || gotHeight >= _requestedHeight) return null;

            Note = $"only {gotHeight}p offered by YouTube (wanted {_requestedHeight}p)";
            return $"[ENGINE] YouTube offered only legacy format {ids} ({gotHeight}p) although {_requestedHeight}p was requested. " +
                   "See the 'PO Token' / 'SABR' warnings above for the reason. Fixes: update yt-dlp (UpdateChannel in settings.json), " +
                   "or set YoutubePlayerClients there as a temporary override.";
        }
    }
}

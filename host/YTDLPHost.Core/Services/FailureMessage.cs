namespace YTDLPHost.Services
{
    /// <summary>What went wrong, as the runner saw it when yt-dlp ended unsuccessfully.</summary>
    /// <param name="ErrorText">Everything yt-dlp wrote to stderr.</param>
    /// <param name="BulkNote">Summary of a playlist / bulk job, e.g. "2 saved, 1 failed".</param>
    /// <param name="LastErrorLine">The last "ERROR:" line seen during a bulk job.</param>
    public sealed record FailureContext(
        string ErrorText,
        int ExitCode,
        bool IsYoutube,
        bool UsedCookies,
        bool CookiesLoggedIn,
        bool IsBulk,
        int FailedItems,
        int DrmItems,
        string BulkNote,
        string LastErrorLine);

    /// <summary>
    /// The plain-language error shown for a failed download. Moved unchanged out of YtDlpRunner in Phase 1.6 so each
    /// wording can be tested; the raw yt-dlp lines always stay in the log.
    /// </summary>
    public static class FailureMessage
    {
        // "Sign in to confirm you're not a bot" - the apostrophe may arrive garbled, so match the stable parts only.
        internal static bool IsBotCheckError(string text) =>
            text.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("not a bot", StringComparison.OrdinalIgnoreCase);

        public static string Build(FailureContext c)
        {
            string errorText = c.ErrorText ?? "";
            string lastErrors = string.Join("\n", errorText
                .Split('\n')
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .TakeLast(3));

            if (errorText.Contains("only works when logged-in", StringComparison.OrdinalIgnoreCase))
            {
                // yt-dlp's own wording (Vimeo, Oct 2026). Say what to do in plain language; the raw line stays in the log.
                return "Vimeo needs you to be logged in. Log in to vimeo.com in your browser, reload the video page and try the download again.";
            }

            if (c.IsYoutube && IsBotCheckError(errorText))
            {
                bool rateLimited = errorText.Contains("429", StringComparison.Ordinal) ||
                                   errorText.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);

                if (!c.UsedCookies)
                    return "YouTube is asking for a sign-in (bot check). Turn on \"Bypass Age & Bot Lock\" while you are signed in to YouTube in this browser, then start the download again from the video page.";
                if (!c.CookiesLoggedIn)
                    return "YouTube treated this as a signed-out session: no YouTube login cookie (SAPISID) reached the downloader. Sign in to YouTube in this browser, reload the extension and the video page, then try again.";
                if (rateLimited)
                    return "YouTube is rate-limiting this connection (HTTP 429), so the bot check was triggered even with your login. Stop retrying for a while (15-60 minutes), avoid VPN/shared networks, then try again.";
                return "YouTube still asked for a sign-in (bot check) even with your login. Reload the video page and try again; if it keeps happening your session cookies may have been rotated or this network is being flagged.";
            }

            if (c.IsBulk && c.DrmItems > 0 && c.DrmItems == c.FailedItems)
                return $"None of the {c.FailedItems} item(s) could be downloaded: they are DRM-protected (e.g. SoundCloud Go+ / protected tracks), which yt-dlp cannot download.";

            if (c.IsBulk && c.FailedItems > 0)
                return string.IsNullOrWhiteSpace(c.LastErrorLine) ? c.BulkNote : $"{c.BulkNote}. Last error: {c.LastErrorLine}";

            return string.IsNullOrWhiteSpace(lastErrors)
                ? $"Download failed (Code: {c.ExitCode}). Check logs for details."
                : lastErrors;
        }
    }
}

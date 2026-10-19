using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>The plain-language error texts (moved out of the runner in Phase 1.6): one test per wording and per branch.</summary>
public class FailureMessageTests
{
    private const string BotCheck = "ERROR: [youtube] abc: Sign in to confirm you're not a bot. Use --cookies-from-browser or --cookies";

    private static FailureContext Ctx(
        string error = "", int exit = 1, bool youtube = false, bool cookies = false, bool loggedIn = false,
        bool bulk = false, int failed = 0, int drm = 0, string note = "", string lastError = "") =>
        new(error, exit, youtube, cookies, loggedIn, bulk, failed, drm, note, lastError);

    [Fact]
    public void Vimeo_login_requirement_is_explained()
    {
        var message = FailureMessage.Build(Ctx("ERROR: [vimeo] 1: This video only works when logged-in"));

        Assert.StartsWith("Vimeo needs you to be logged in.", message);
    }

    [Fact]
    public void A_bot_check_without_cookies_tells_the_user_to_turn_on_the_bypass()
    {
        var message = FailureMessage.Build(Ctx(BotCheck, youtube: true, cookies: false));

        Assert.StartsWith("YouTube is asking for a sign-in (bot check).", message);
        Assert.Contains("Bypass Age & Bot Lock", message);
    }

    [Fact]
    public void A_bot_check_with_cookies_but_no_login_says_the_session_was_signed_out()
    {
        var message = FailureMessage.Build(Ctx(BotCheck, youtube: true, cookies: true, loggedIn: false));

        Assert.StartsWith("YouTube treated this as a signed-out session", message);
    }

    [Theory]
    [InlineData("HTTP Error 429")]
    [InlineData("Too Many Requests")]
    public void A_bot_check_with_a_login_and_rate_limiting_says_to_wait(string rateLimitText)
    {
        var message = FailureMessage.Build(Ctx(BotCheck + "\n" + rateLimitText, youtube: true, cookies: true, loggedIn: true));

        Assert.StartsWith("YouTube is rate-limiting this connection", message);
    }

    [Fact]
    public void A_bot_check_with_a_login_and_no_rate_limit_says_to_reload()
    {
        var message = FailureMessage.Build(Ctx(BotCheck, youtube: true, cookies: true, loggedIn: true));

        Assert.StartsWith("YouTube still asked for a sign-in", message);
    }

    [Fact]
    public void The_bot_check_wording_is_only_used_for_youtube()
    {
        var message = FailureMessage.Build(Ctx(BotCheck, youtube: false));

        Assert.Equal(BotCheck, message); // falls through to the raw last lines
    }

    [Fact]
    public void A_bulk_job_where_every_failure_is_drm_says_so()
    {
        var message = FailureMessage.Build(Ctx(bulk: true, failed: 3, drm: 3, note: "3 failed (DRM-protected)"));

        Assert.StartsWith("None of the 3 item(s) could be downloaded: they are DRM-protected", message);
    }

    [Fact]
    public void A_bulk_job_with_failures_shows_the_summary_and_the_last_error()
    {
        var message = FailureMessage.Build(Ctx(bulk: true, failed: 2, drm: 1, note: "1 saved, 2 failed", lastError: "ERROR: private video"));

        Assert.Equal("1 saved, 2 failed. Last error: ERROR: private video", message);
    }

    [Fact]
    public void A_bulk_job_without_an_error_line_shows_just_the_summary()
    {
        var message = FailureMessage.Build(Ctx(bulk: true, failed: 2, note: "2 failed"));

        Assert.Equal("2 failed", message);
    }

    [Fact]
    public void Otherwise_the_last_three_non_blank_error_lines_are_shown()
    {
        var message = FailureMessage.Build(Ctx("one\ntwo\n\nthree\nfour\n"));

        Assert.Equal("two\nthree\nfour", message);
    }

    [Fact]
    public void With_no_error_text_the_exit_code_is_reported()
    {
        var message = FailureMessage.Build(Ctx("", exit: 2));

        Assert.Equal("Download failed (Code: 2). Check logs for details.", message);
    }
}

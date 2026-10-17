using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>
/// Pure helpers that came in from main's YouTube bot-check / quality work and the update-channel setting.
/// The runner's stateful YouTube logic (quality guard, error wording) still needs a refactor before it can be
/// unit-tested; see PLAN.md step 1.6.
/// </summary>
public class YoutubeAndUpdateChannelTests
{
    // ---------------- IsYoutubeCommand ----------------

    [Theory]
    [InlineData("yt-dlp \"https://www.youtube.com/watch?v=abcdefghijk\"")]
    [InlineData("yt-dlp \"https://youtu.be/abcdefghijk\"")]
    [InlineData("yt-dlp \"https://m.youtube.com/watch?v=abcdefghijk\"")]
    [InlineData("yt-dlp \"https://music.youtube.com/watch?v=abcdefghijk\"")]
    [InlineData("yt-dlp \"https://www.youtube-nocookie.com/embed/abcdefghijk\"")]
    [InlineData("yt-dlp \"HTTPS://WWW.YOUTUBE.COM/watch?v=abcdefghijk\"")]
    public void Youtube_urls_are_recognised(string command)
    {
        Assert.True(YtDlpRunner.IsYoutubeCommand(command));
    }

    [Theory]
    [InlineData("yt-dlp \"https://vimeo.com/123456789\"")]
    [InlineData("yt-dlp \"https://notyoutube.com/watch?v=abcdefghijk\"")]
    [InlineData("yt-dlp \"https://youtube.com.evil.example/watch\"")]
    [InlineData("")]
    public void Other_sites_are_not_youtube(string command)
    {
        Assert.False(YtDlpRunner.IsYoutubeCommand(command));
    }

    // ---------------- CookieFileHasYoutubeLogin ----------------

    private static string WriteCookieFile(params string[] cookieLines)
    {
        var path = Path.Combine(Path.GetTempPath(), "ytdlp-cookies-test-" + Guid.NewGuid() + ".txt");
        File.WriteAllLines(path, new[] { "# Netscape HTTP Cookie File" }.Concat(cookieLines));
        return path;
    }

    private static string Row(string name, string value) =>
        string.Join("\t", ".youtube.com", "TRUE", "/", "TRUE", "1893456000", name, value);

    [Theory]
    [InlineData("SAPISID")]
    [InlineData("__Secure-3PAPISID")]
    [InlineData("__Secure-1PAPISID")]
    public void A_cookie_file_with_an_auth_cookie_counts_as_logged_in(string cookieName)
    {
        var path = WriteCookieFile(Row("PREF", "x"), Row(cookieName, "abc123"));
        try { Assert.True(YtDlpRunner.CookieFileHasYoutubeLogin(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_cookie_file_without_an_auth_cookie_is_logged_out()
    {
        var path = WriteCookieFile(Row("SID", "abc"), Row("PREF", "x"));
        try { Assert.False(YtDlpRunner.CookieFileHasYoutubeLogin(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void An_auth_cookie_with_an_empty_value_does_not_count()
    {
        var path = WriteCookieFile(Row("SAPISID", ""));
        try { Assert.False(YtDlpRunner.CookieFileHasYoutubeLogin(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_missing_cookie_file_is_logged_out_not_an_error()
    {
        var path = Path.Combine(Path.GetTempPath(), "ytdlp-no-such-cookie-file-" + Guid.NewGuid() + ".txt");

        Assert.False(YtDlpRunner.CookieFileHasYoutubeLogin(path));
    }

    // ---------------- IsBotCheckError ----------------

    [Theory]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm you\u2019re not a bot. Use --cookies-from-browser", true)]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm you're not a bot.", true)]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm you\uFFFDre not a bot.", true)]   // apostrophe garbled by the console code page
    [InlineData("ERROR: [youtube] abc: Sign in to confirm your age.", false)]
    [InlineData("ERROR: HTTP Error 429: Too Many Requests", false)]
    public void The_bot_check_message_is_recognised_even_with_a_garbled_apostrophe(string errorText, bool expected)
    {
        Assert.Equal(expected, YtDlpRunner.IsBotCheckError(errorText));
    }

    // ---------------- update channel ----------------

    [Theory]
    [InlineData("stable", "stable")]
    [InlineData("NIGHTLY", "nightly")]
    [InlineData("  master  ", "master")]
    [InlineData("", "nightly")]
    [InlineData(null, "nightly")]
    [InlineData("beta", "nightly")]
    [InlineData("stable --exec calc", "nightly")]   // the value ends up on a command line, so only the three names may pass
    public void Only_known_update_channels_are_accepted(string? configured, string expected)
    {
        Assert.Equal(expected, EngineProvisioner.NormalizeUpdateChannel(configured));
    }
}

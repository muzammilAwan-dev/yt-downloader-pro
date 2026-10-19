using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>
/// The command string -> argument list step (Phase 1.6). Replaces the old string surgery in the runner; these tests
/// pin down every addition and rewrite it makes, and that values stay single arguments.
/// </summary>
public class YtDlpCommandBuilderTests
{
    private const string FfmpegPath = @"C:\engine\ffmpeg.exe";
    private const string DenoPath = @"C:\engine\deno.exe";
    private const string Vimeo = "\"https://vimeo.com/123456789\"";
    private const string Youtube = "\"https://www.youtube.com/watch?v=abcdefghijk\"";

    private static YtDlpEnvironment Env(string speed = "0", string clients = "", string? cookies = null, string? deno = DenoPath) =>
        new(FfmpegPath, deno, speed, clients, cookies);

    private static YtDlpInvocation Build(string command, YtDlpEnvironment? env = null) =>
        YtDlpCommandBuilder.Build(command, env ?? Env());

    [Fact]
    public void A_typical_command_becomes_one_argument_per_value_plus_the_engine_paths()
    {
        var invocation = Build("yt-dlp -f \"bv*[height<=1080]+ba/b\" --merge-output-format mp4 -N 4 --no-playlist --restrict-filenames " +
                               "--embed-thumbnail --no-warnings --progress -o \"~/Downloads/%(title)s_1080p.mp4\" " + Vimeo);

        Assert.Equal(new[]
        {
            "-f", "bv*[height<=1080]+ba/b", "--merge-output-format", "mp4", "-N", "4", "--no-playlist", "--restrict-filenames",
            "--embed-thumbnail", "--no-warnings", "--progress", "-o", "~/Downloads/%(title)s_1080p.mp4", "https://vimeo.com/123456789",
            "--ffmpeg-location", FfmpegPath, "--js-runtimes", "deno:" + DenoPath
        }, invocation.Arguments.ToArray());
        Assert.False(invocation.IsYoutube);
    }

    [Theory]
    [InlineData("yt-dlp")]
    [InlineData("yt-dlp.exe")]
    [InlineData("YT-DLP.EXE")]
    [InlineData("")]
    public void The_executable_name_in_front_is_dropped(string prefix)
    {
        var invocation = Build(prefix + " " + Vimeo);

        Assert.Equal("https://vimeo.com/123456789", invocation.Arguments[0]);
    }

    [Fact]
    public void A_strict_audio_request_gets_a_fallback()
    {
        var invocation = Build("yt-dlp -f \"ba\" " + Vimeo);

        Assert.Equal("ba/b", invocation.Arguments[1]);
        Assert.Contains(invocation.AppLog, line => line.Contains("Upgraded strict Audio request"));
    }

    [Theory]
    [InlineData("ba/b")]
    [InlineData("bv*+ba")]
    public void Other_format_requests_are_left_alone(string format)
    {
        var invocation = Build("yt-dlp -f \"" + format + "\" " + Vimeo);

        Assert.Equal(format, invocation.Arguments[1]);
        Assert.Empty(invocation.AppLog);
    }

    // ---------------- thumbnails for extracted audio ----------------

    [Fact]
    public void Extracted_audio_gets_an_embedded_thumbnail()
    {
        var invocation = Build("yt-dlp --extract-audio --audio-format mp3 " + Vimeo);

        Assert.Equal(1, invocation.Arguments.Count(a => a == "--embed-thumbnail"));
    }

    [Fact]
    public void Wav_audio_gets_no_thumbnail_because_wav_cannot_hold_one()
    {
        var invocation = Build("yt-dlp --extract-audio --audio-format wav " + Vimeo);

        Assert.DoesNotContain("--embed-thumbnail", invocation.Arguments);
    }

    [Fact]
    public void An_existing_thumbnail_flag_is_not_duplicated()
    {
        var invocation = Build("yt-dlp --extract-audio --embed-thumbnail " + Vimeo);

        Assert.Equal(1, invocation.Arguments.Count(a => a == "--embed-thumbnail"));
    }

    [Fact]
    public void A_link_that_happens_to_contain_wav_does_not_block_the_thumbnail()
    {
        var invocation = Build("yt-dlp --extract-audio \"https://example.com/wav-lessons\"");

        Assert.Contains("--embed-thumbnail", invocation.Arguments);
    }

    // ---------------- engine paths and limits ----------------

    [Fact]
    public void Deno_is_only_passed_when_it_is_installed()
    {
        var invocation = Build("yt-dlp " + Vimeo, Env(deno: null));

        Assert.DoesNotContain("--js-runtimes", invocation.Arguments);
    }

    [Theory]
    [InlineData("2M", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    public void The_speed_limit_is_added_only_when_one_is_set(string speed, bool expected)
    {
        var invocation = Build("yt-dlp " + Vimeo, Env(speed: speed));

        Assert.Equal(expected, invocation.Arguments.Contains("--limit-rate"));
        if (expected) Assert.Equal(speed, invocation.Arguments[invocation.Arguments.ToList().IndexOf("--limit-rate") + 1]);
    }

    [Fact]
    public void A_limit_already_in_the_command_wins()
    {
        var invocation = Build("yt-dlp --limit-rate 500K " + Vimeo, Env(speed: "2M"));

        Assert.Equal(1, invocation.Arguments.Count(a => a == "--limit-rate"));
        Assert.Contains("500K", invocation.Arguments);
        Assert.DoesNotContain("2M", invocation.Arguments);
    }

    [Fact]
    public void Cookies_are_passed_as_a_separate_pair_and_never_twice()
    {
        var withCookies = Build("yt-dlp " + Vimeo, Env(cookies: @"C:\temp\cookies.txt"));
        var args = withCookies.Arguments.ToList();
        Assert.Equal(@"C:\temp\cookies.txt", args[args.IndexOf("--cookies") + 1]);

        var alreadyThere = Build("yt-dlp --cookies \"mine.txt\" " + Vimeo, Env(cookies: @"C:\temp\cookies.txt"));
        Assert.Equal(1, alreadyThere.Arguments.Count(a => a == "--cookies"));
    }

    // ---------------- YouTube rules ----------------

    [Fact]
    public void Youtube_keeps_yt_dlps_warnings_and_reports_the_requested_height()
    {
        var invocation = Build("yt-dlp -f \"bv*[height<=720]+ba/b\" --no-warnings " + Youtube);

        Assert.True(invocation.IsYoutube);
        Assert.Equal(720, invocation.RequestedHeight);
        Assert.DoesNotContain("--no-warnings", invocation.Arguments);
    }

    [Fact]
    public void Other_sites_keep_no_warnings()
    {
        var invocation = Build("yt-dlp --no-warnings " + Vimeo);

        Assert.Contains("--no-warnings", invocation.Arguments);
    }

    [Fact]
    public void A_valid_player_client_override_is_passed_for_youtube()
    {
        var invocation = Build("yt-dlp " + Youtube, Env(clients: " default,tv "));
        var args = invocation.Arguments.ToList();

        Assert.Equal("youtube:player_client=default,tv", args[args.IndexOf("--extractor-args") + 1]);
        Assert.Contains(invocation.TaskLog, line => line.Contains("player_client override"));
    }

    [Theory]
    [InlineData("a;b")]
    [InlineData("tv --exec calc")]
    [InlineData("tv\"")]
    public void An_invalid_player_client_override_is_ignored(string clients)
    {
        var invocation = Build("yt-dlp " + Youtube, Env(clients: clients));

        Assert.DoesNotContain("--extractor-args", invocation.Arguments);
    }

    [Fact]
    public void The_player_client_override_is_not_used_for_other_sites()
    {
        var invocation = Build("yt-dlp " + Vimeo, Env(clients: "default,tv"));

        Assert.DoesNotContain("--extractor-args", invocation.Arguments);
    }

    // ---------------- download sections ----------------

    [Theory]
    [InlineData("*01:15-01:25", "01:15", "01:25")]
    [InlineData("10-20.5", "10", "20.5")]
    public void The_cut_range_is_reported(string section, string start, string end)
    {
        var invocation = Build("yt-dlp --download-sections \"" + section + "\" " + Vimeo);

        Assert.Equal(start, invocation.SectionStart);
        Assert.Equal(end, invocation.SectionEnd);
    }

    [Fact]
    public void Without_a_section_there_is_no_range()
    {
        var invocation = Build("yt-dlp " + Vimeo);

        Assert.Null(invocation.SectionStart);
        Assert.Null(invocation.SectionEnd);
    }

    // ---------------- safety ----------------

    [Fact]
    public void A_quoted_value_with_spaces_and_flag_like_text_stays_one_argument()
    {
        var invocation = Build("yt-dlp --user-agent \"Mozilla/5.0 (X11; Linux) --exec calc\" " + Vimeo);

        Assert.Contains("Mozilla/5.0 (X11; Linux) --exec calc", invocation.Arguments);
        Assert.DoesNotContain("--exec", invocation.Arguments);
    }

    [Fact]
    public void An_unclosed_quote_is_an_error_not_a_guess()
    {
        Assert.Throws<FormatException>(() => Build("yt-dlp -o \"~/Downloads/x.mp4 " + Vimeo));
    }

    [Fact]
    public void The_display_string_quotes_only_arguments_with_spaces()
    {
        var invocation = Build("yt-dlp -o \"~/My Videos/%(title)s.mp4\" " + Vimeo);

        Assert.Contains("-o \"~/My Videos/%(title)s.mp4\" https://vimeo.com/123456789", invocation.ToDisplayString());
    }
}

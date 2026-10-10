using System.Text;
using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

public class CommandIngestTests : IDisposable
{
    private const string Cmd = "yt-dlp \"https://www.youtube.com/watch?v=dQw4w9WgXcQ\"";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ytdlp-ingest-tests-" + Guid.NewGuid());

    private sealed class TempPaths : IAppPaths
    {
        private readonly string _root;
        public TempPaths(string root) => _root = root;
        public string HomeDir => Path.Combine(_root, "home");
        public string DownloadsDir => Path.Combine(HomeDir, "Downloads");
        public string EngineDir => Path.Combine(_root, "engine");
        public string DataDir => Path.Combine(_root, "data");
        public string LogsDir => Path.Combine(_root, "logs");
        public string PayloadsDir => Path.Combine(_root, "payloads");
        public string TempDir => Path.Combine(_root, "tmp");
    }

    private CommandIngest Ingest() => new(new TempPaths(_root));

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    /// <summary>Builds ytdlp://&lt;cmd&gt;[||&lt;cookies&gt;[||&lt;user-agent&gt;]] exactly like the extension does.</summary>
    private static string Link(string command, string? cookies = null, string? userAgent = null)
    {
        var s = "ytdlp://" + B64(command);
        if (cookies != null || userAgent != null) s += "||" + (cookies == null ? "" : B64(cookies));
        if (userAgent != null) s += "||" + B64(userAgent);
        return s;
    }

    private ParsedPayload Parsed(string link)
    {
        var r = Ingest().Parse(link);
        Assert.Equal(IngestStatus.Parsed, r.Status);
        return r.Payload!;
    }

    // ---------------- Parse ----------------

    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("ytdlp://")]
    [InlineData("ytdlp:///")]
    [InlineData("ytdlp://   ")]
    public void Links_that_are_not_a_payload_are_ignored(string link)
    {
        Assert.Equal(IngestStatus.Ignored, Ingest().Parse(link).Status);
    }

    [Fact]
    public void A_valid_link_is_parsed()
    {
        var link = Link(Cmd);
        var payload = Parsed(link);

        Assert.Equal(Cmd, payload.Command);
        Assert.Equal(link, payload.UrlPayload);
        Assert.Null(payload.CookiePart);
        Assert.Null(payload.UserAgentPart);
    }

    [Fact]
    public void Windows_trailing_slash_and_percent_encoding_are_tolerated()
    {
        var link = Link(Cmd).Replace("=", "%3D") + "/";
        Assert.Equal(Cmd, Parsed(link).Command);
    }

    [Fact]
    public void The_scheme_is_case_insensitive()
    {
        Assert.Equal(Cmd, Parsed("YTDLP://" + B64(Cmd)).Command);
    }

    [Fact]
    public void Cookie_and_user_agent_parts_are_captured_when_present()
    {
        var payload = Parsed(Link(Cmd, cookies: "c", userAgent: "ua"));

        Assert.Equal(B64("c"), payload.CookiePart);
        Assert.Equal(B64("ua"), payload.UserAgentPart);
    }

    [Fact]
    public void An_empty_cookie_part_is_treated_as_absent()
    {
        var payload = Parsed(Link(Cmd, cookies: null, userAgent: "ua"));

        Assert.Null(payload.CookiePart);
        Assert.NotNull(payload.UserAgentPart);
    }

    [Theory]
    [InlineData("yt-dlp --exec calc \"https://a.com\"", "--exec")]
    [InlineData("yt-dlp --external-downloader curl \"https://a.com\"", "--external-downloader")]
    [InlineData("curl https://a.com", "must start with yt-dlp")]
    [InlineData("yt-dlp -o \"C:\\Windows\\x.mp4\" \"https://a.com\"", "output path")]
    public void Dangerous_commands_are_blocked_with_a_reason(string command, string reasonPart)
    {
        var r = Ingest().Parse(Link(command, cookies: "c", userAgent: "ua"));

        Assert.Equal(IngestStatus.Blocked, r.Status);
        Assert.Null(r.Payload);
        Assert.Contains(reasonPart, r.BlockReason);
    }

    [Fact]
    public void Malformed_base64_in_the_command_part_throws_so_the_caller_can_log_and_drop_it()
    {
        Assert.ThrowsAny<FormatException>(() => Ingest().Parse("ytdlp://!!!!"));
    }

    // ---------------- Prepare ----------------

    [Fact]
    public void Cookies_are_written_to_a_temp_file_without_a_byte_order_mark()
    {
        const string cookies = "\uFEFF# Netscape HTTP Cookie File\n.example.com\tTRUE\t/\tFALSE\t0\tsid\tabc\n";

        var prepared = Ingest().Prepare(Parsed(Link(Cmd, cookies: cookies)));

        Assert.True(File.Exists(prepared.CookieFilePath));
        Assert.Equal(Path.Combine(_root, "tmp"), Path.GetDirectoryName(prepared.CookieFilePath));
        Assert.Equal((byte)'#', File.ReadAllBytes(prepared.CookieFilePath)[0]);
        Assert.StartsWith("# Netscape", prepared.CookieContent);
        Assert.Equal(Cmd, prepared.Command);
    }

    [Fact]
    public void No_cookie_part_means_no_file_and_an_empty_path()
    {
        var prepared = Ingest().Prepare(Parsed(Link(Cmd)));

        Assert.Equal(string.Empty, prepared.CookieFilePath);
        Assert.Null(prepared.CookieContent);
    }

    [Fact]
    public void A_broken_cookie_part_is_swallowed_and_the_command_is_unchanged()
    {
        var payload = Parsed(Link(Cmd)) with { CookiePart = "!!!!" };

        var prepared = Ingest().Prepare(payload);

        Assert.Equal(Cmd, prepared.Command);
        Assert.Equal(string.Empty, prepared.CookieFilePath);
    }

    [Fact]
    public void The_browser_user_agent_is_appended_to_the_command()
    {
        const string ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

        var prepared = Ingest().Prepare(Parsed(Link(Cmd, userAgent: ua)));

        Assert.Equal($"{Cmd} --user-agent \"{ua}\"", prepared.Command);
        Assert.True(CommandValidator.TryValidate(prepared.Command, out _));
    }

    [Fact]
    public void An_existing_user_agent_flag_is_not_duplicated()
    {
        var command = Cmd + " --user-agent \"Mine/1.0\"";

        var prepared = Ingest().Prepare(Parsed(Link(command, userAgent: "Other/2.0")));

        Assert.Equal(command, prepared.Command);
    }

    // ---------------- security regression ----------------

    [Theory]
    [InlineData("x\" --exec \"calc.exe")]                         // quote break-out: adds --exec after validation
    [InlineData("x\" --exec calc \"")]
    [InlineData("x\" -o \"C:\\Windows\\evil.mp4")]                // break-out to an output path the validator forbids
    [InlineData("x\\\" --exec calc")]                             // escaped-quote variant
    [InlineData("Mozilla/5.0\r\n--exec calc")]                    // control characters
    [InlineData("Mozilla/5.0 \u00E9")]                            // non-ASCII
    public void A_hostile_user_agent_can_never_add_flags(string hostileUserAgent)
    {
        var payload = Parsed(Link(Cmd, userAgent: hostileUserAgent));

        var prepared = Ingest().Prepare(payload);

        Assert.Equal(Cmd, prepared.Command);                       // the User-Agent is skipped entirely
        Assert.DoesNotContain("--exec", prepared.Command);
        Assert.True(CommandValidator.TryValidate(prepared.Command, out _));
    }

    [Fact]
    public void An_oversized_user_agent_is_skipped()
    {
        var prepared = Ingest().Prepare(Parsed(Link(Cmd, userAgent: new string('A', 600))));

        Assert.Equal(Cmd, prepared.Command);
    }

    [Fact]
    public void A_broken_user_agent_part_is_swallowed()
    {
        var payload = Parsed(Link(Cmd)) with { UserAgentPart = "!!!!" };

        Assert.Equal(Cmd, Ingest().Prepare(payload).Command);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

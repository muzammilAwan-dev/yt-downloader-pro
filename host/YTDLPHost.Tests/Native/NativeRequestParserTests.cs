using System.Text;
using Xunit;
using YTDLPHost.Native;

namespace YTDLPHost.Tests.Native;

/// <summary>The strict parser for browser messages (Phase 2, step 2.1): what is accepted, and above all what is refused.</summary>
public class NativeRequestParserTests
{
    private const string Url = "https://www.youtube.com/watch?v=abcdefghijk";

    private static NativeParseResult Parse(string json) => NativeRequestParser.Parse(Encoding.UTF8.GetBytes(json));

    /// <summary>A valid download message; <paramref name="options"/> is the inside of the options object.</summary>
    private static string Download(string options = "\"quality\":\"1080\"", string extra = "") =>
        $"{{\"v\":1,\"id\":\"d1\",\"type\":\"download\",\"urls\":[\"{Url}\"],\"options\":{{{options}}}{extra}}}";

    private static NativeParseError Refused(string json)
    {
        var result = Parse(json);
        Assert.False(result.Ok);
        return Assert.IsType<NativeParseError>(result.Error);
    }

    private static DownloadRequest Accepted(string json)
    {
        var result = Parse(json);
        Assert.True(result.Ok, result.Error?.Message);
        return Assert.IsType<DownloadRequest>(result.Request);
    }

    // ---------------------------------------------------------------- valid messages

    [Fact]
    public void A_hello_is_accepted()
    {
        var result = Parse("{\"v\":1,\"id\":\"h-1\",\"type\":\"hello\"}");

        Assert.True(result.Ok);
        Assert.Equal("h-1", Assert.IsType<HelloRequest>(result.Request).Id);
    }

    [Fact]
    public void A_minimal_download_gets_the_defaults()
    {
        var request = Accepted(Download());

        Assert.Equal("d1", request.Id);
        Assert.Equal(new[] { Url }, request.Urls);
        Assert.Null(request.Auth);
        var o = request.Options;
        Assert.Equal("1080", o.Quality);
        Assert.Equal("mp3", o.AudioFormat);
        Assert.False(o.Subs);
        Assert.False(o.Compat);
        Assert.False(o.SponsorBlock);
        Assert.False(o.EmbedMetadata);
        Assert.True(o.EmbedThumbnail);
        Assert.False(o.Playlist);
        Assert.Null(o.PlaylistItems);
        Assert.Null(o.Trim);
        Assert.Equal(4, o.ConcurrentFragments);
        Assert.Null(o.Custom);
        Assert.Equal("video", o.Job);
        Assert.Null(o.ArchiveName);
        Assert.True(o.CropKeyframeCuts);
        Assert.False(o.UniqueFilename);
    }

    [Fact]
    public void The_two_site_flags_are_read()
    {
        var o = Accepted(Download("\"quality\":\"1080\",\"cropKeyframeCuts\":false,\"uniqueFilename\":true")).Options;

        Assert.False(o.CropKeyframeCuts);
        Assert.True(o.UniqueFilename);
    }

    [Theory]
    [InlineData("\"quality\":\"custom\",\"custom\":\"-f b\"")]
    [InlineData("\"quality\":\"1080\",\"subs\":true")]
    [InlineData("\"quality\":\"1080\",\"sponsorBlock\":true")]
    [InlineData("\"quality\":\"1080\",\"playlist\":true")]
    [InlineData("\"quality\":\"1080\",\"trim\":{\"start\":\"1\"}")]
    public void A_channel_job_refuses_single_video_options(string options)
    {
        var json = Download(options + ",\"job\":\"channel\",\"archiveName\":\"some_channel\"");

        Assert.Equal(NativeErrorCode.BadField, Refused(json).Code);
    }

    [Fact]
    public void Every_option_is_read_correctly()
    {
        var request = Accepted(Download(
            "\"quality\":\"audio\",\"audioFormat\":\"flac\",\"subs\":true,\"compat\":true,\"sponsorBlock\":true,\"embedMetadata\":true," +
            "\"embedThumbnail\":false,\"playlist\":true,\"playlistItems\":\"1-5,8\",\"trim\":{\"start\":\"01:15\",\"end\":\"01:25.5\"}," +
            "\"concurrentFragments\":16",
            ",\"auth\":{\"cookies\":\"# Netscape HTTP Cookie File\\n.youtube.com\\tTRUE\\t/\\tTRUE\\t0\\tSAPISID\\tabc\",\"userAgent\":\"Mozilla/5.0 (X11; Linux x86_64)\"}"));

        var o = request.Options;
        Assert.Equal("audio", o.Quality);
        Assert.Equal("flac", o.AudioFormat);
        Assert.True(o.Subs && o.Compat && o.SponsorBlock && o.EmbedMetadata && o.Playlist);
        Assert.False(o.EmbedThumbnail);
        Assert.Equal("1-5,8", o.PlaylistItems);
        Assert.Equal(new TrimRange("01:15", "01:25.5"), o.Trim);
        Assert.Equal(16, o.ConcurrentFragments);
        Assert.Contains("SAPISID", request.Auth!.Cookies);
        Assert.Equal("Mozilla/5.0 (X11; Linux x86_64)", request.Auth.UserAgent);
    }

    [Fact]
    public void A_custom_command_and_a_channel_job_are_read()
    {
        var custom = Accepted(Download("\"quality\":\"custom\",\"custom\":\"-f best --no-playlist\""));
        Assert.Equal("-f best --no-playlist", custom.Options.Custom);

        var channel = Accepted(Download("\"quality\":\"720\",\"job\":\"channel\",\"archiveName\":\"example.com_channel\""));
        Assert.Equal("channel", channel.Options.Job);
        Assert.Equal("example.com_channel", channel.Options.ArchiveName);
    }

    [Fact]
    public void A_quality_height_is_normalised()
    {
        Assert.Equal("720", Accepted(Download("\"quality\":\"0720\"")).Options.Quality);
    }

    [Fact]
    public void A_json_null_counts_as_absent()
    {
        var request = Accepted(Download("\"quality\":\"1080\",\"trim\":null,\"custom\":null", ",\"auth\":null"));

        Assert.Null(request.Options.Trim);
        Assert.Null(request.Auth);
    }

    [Fact]
    public void Empty_trim_and_empty_auth_objects_count_as_absent()
    {
        var request = Accepted(Download("\"quality\":\"1080\",\"trim\":{\"start\":\"\",\"end\":\"\"}", ",\"auth\":{\"cookies\":\"\"}"));

        Assert.Null(request.Options.Trim);
        Assert.Null(request.Auth);
    }

    // ---------------------------------------------------------------- the envelope

    [Fact]
    public void Broken_json_is_refused_without_an_id()
    {
        var result = Parse("{\"v\":1,");

        Assert.False(result.Ok);
        Assert.Equal(NativeErrorCode.BadJson, result.Error!.Code);
        Assert.Null(result.Id);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"hello\"")]
    public void Something_other_than_an_object_is_refused(string json)
    {
        Assert.Equal(NativeErrorCode.BadField, Refused(json).Code);
    }

    [Fact]
    public void Text_that_is_not_utf8_is_refused()
    {
        var result = NativeRequestParser.Parse(new byte[] { 0x7B, 0x22, 0xFF, 0xFE, 0x22, 0x7D });

        Assert.False(result.Ok);
        Assert.Equal(NativeErrorCode.BadJson, result.Error!.Code);
    }

    [Fact]
    public void Deeply_nested_json_is_refused()
    {
        var json = "{\"v\":1,\"id\":\"d1\",\"type\":\"hello\",\"x\":" + new string('[', 40) + new string(']', 40) + "}";

        Assert.Equal(NativeErrorCode.BadJson, Refused(json).Code);
    }

    [Fact]
    public void A_different_protocol_version_is_refused_but_the_id_is_still_reported()
    {
        var result = Parse("{\"v\":2,\"id\":\"d9\",\"type\":\"hello\"}");

        Assert.False(result.Ok);
        Assert.Equal(NativeErrorCode.BadVersion, result.Error!.Code);
        Assert.Equal("d9", result.Id);
    }

    [Fact]
    public void A_missing_or_non_numeric_version_is_refused()
    {
        Assert.Equal("v", Refused("{\"id\":\"d1\",\"type\":\"hello\"}").Field);
        Assert.Equal("v", Refused("{\"v\":\"1\",\"id\":\"d1\",\"type\":\"hello\"}").Field);
    }

    [Theory]
    [InlineData("{\"v\":1,\"type\":\"hello\"}")]
    [InlineData("{\"v\":1,\"id\":\"\",\"type\":\"hello\"}")]
    [InlineData("{\"v\":1,\"id\":\"has space\",\"type\":\"hello\"}")]
    [InlineData("{\"v\":1,\"id\":\"semi;colon\",\"type\":\"hello\"}")]
    [InlineData("{\"v\":1,\"id\":5,\"type\":\"hello\"}")]
    public void A_missing_or_unusable_id_is_refused(string json)
    {
        var result = Parse(json);

        Assert.False(result.Ok);
        Assert.Equal("id", result.Error!.Field);
        Assert.Null(result.Id);
    }

    [Fact]
    public void An_id_longer_than_64_characters_is_refused()
    {
        Assert.Equal("id", Refused("{\"v\":1,\"id\":\"" + new string('a', 65) + "\",\"type\":\"hello\"}").Field);
    }

    [Fact]
    public void An_unknown_message_type_is_refused()
    {
        var error = Refused("{\"v\":1,\"id\":\"d1\",\"type\":\"format-disk\"}");

        Assert.Equal(NativeErrorCode.BadType, error.Code);
    }

    [Fact]
    public void Unknown_fields_are_refused_everywhere()
    {
        Assert.Equal("extra", Refused(Download(extra: ",\"extra\":1")).Field);
        Assert.Equal("options.shell", Refused(Download("\"quality\":\"1080\",\"shell\":\"calc\"")).Field);
        Assert.Equal("options.trim.step", Refused(Download("\"quality\":\"1080\",\"trim\":{\"start\":\"1\",\"step\":\"2\"}")).Field);
        Assert.Equal("auth.token", Refused(Download(extra: ",\"auth\":{\"token\":\"x\"}")).Field);
        Assert.Equal("urls", Refused("{\"v\":1,\"id\":\"h1\",\"type\":\"hello\",\"urls\":[]}").Field);
    }

    [Fact]
    public void A_field_that_appears_twice_is_refused()
    {
        var error = Refused("{\"v\":1,\"id\":\"d1\",\"id\":\"d2\",\"type\":\"hello\"}");

        Assert.Equal("id", error.Field);
        Assert.Contains("more than once", error.Message);
    }

    // ---------------------------------------------------------------- urls

    [Theory]
    [InlineData("ftp://example.com/video")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/system32/config")]
    [InlineData("-o C:/evil.mp4")]
    [InlineData("--exec calc")]
    [InlineData("https://example.com/a b")]
    [InlineData("https://example.com/a\\nb")]
    [InlineData("example.com/video")]
    [InlineData("https://")]
    [InlineData("")]
    public void Anything_but_a_plain_http_or_https_url_is_refused(string url)
    {
        var json = $"{{\"v\":1,\"id\":\"d1\",\"type\":\"download\",\"urls\":[\"{url}\"],\"options\":{{\"quality\":\"1080\"}}}}";

        Assert.Equal("urls[0]", Refused(json).Field);
    }

    [Fact]
    public void A_double_quote_inside_a_url_is_refused()
    {
        var json = "{\"v\":1,\"id\":\"d1\",\"type\":\"download\",\"urls\":[\"https://example.com/a\\\"b\"],\"options\":{\"quality\":\"1080\"}}";

        Assert.Equal("urls[0]", Refused(json).Field);
    }

    [Fact]
    public void The_urls_field_must_be_a_non_empty_array_of_at_most_500_strings()
    {
        Assert.Equal("urls", Refused("{\"v\":1,\"id\":\"d1\",\"type\":\"download\",\"urls\":[],\"options\":{\"quality\":\"1080\"}}").Field);
        Assert.Equal("urls", Refused("{\"v\":1,\"id\":\"d1\",\"type\":\"download\",\"urls\":\"" + Url + "\",\"options\":{\"quality\":\"1080\"}}").Field);
        Assert.Equal("urls[0]", Refused("{\"v\":1,\"id\":\"d1\",\"type\":\"download\",\"urls\":[5],\"options\":{\"quality\":\"1080\"}}").Field);

        var many = string.Join(",", Enumerable.Range(0, NativeProtocol.MaxUrls + 1).Select(i => $"\"https://example.com/{i}\""));
        Assert.Equal("urls", Refused($"{{\"v\":1,\"id\":\"d1\",\"type\":\"download\",\"urls\":[{many}],\"options\":{{\"quality\":\"1080\"}}}}").Field);

        var exactly = string.Join(",", Enumerable.Range(0, NativeProtocol.MaxUrls).Select(i => $"\"https://example.com/{i}\""));
        Assert.Equal(NativeProtocol.MaxUrls, Accepted($"{{\"v\":1,\"id\":\"d1\",\"type\":\"download\",\"urls\":[{exactly}],\"options\":{{\"quality\":\"1080\"}}}}").Urls.Count);
    }

    [Fact]
    public void A_missing_options_object_is_refused()
    {
        Assert.Equal("options", Refused($"{{\"v\":1,\"id\":\"d1\",\"type\":\"download\",\"urls\":[\"{Url}\"]}}").Field);
    }

    // ---------------------------------------------------------------- options

    [Theory]
    [InlineData("\"quality\":\"50\"")]
    [InlineData("\"quality\":\"9999\"")]
    [InlineData("\"quality\":\"1080p\"")]
    [InlineData("\"quality\":\"best\"")]
    [InlineData("\"quality\":\"-720\"")]
    [InlineData("\"quality\":\"\"")]
    [InlineData("\"quality\":1080")]
    [InlineData("\"audioFormat\":\"exe\",\"quality\":\"audio\"")]
    [InlineData("\"quality\":\"1080\",\"subs\":\"yes\"")]
    [InlineData("\"quality\":\"1080\",\"concurrentFragments\":0")]
    [InlineData("\"quality\":\"1080\",\"concurrentFragments\":17")]
    [InlineData("\"quality\":\"1080\",\"concurrentFragments\":\"4\"")]
    [InlineData("\"quality\":\"1080\",\"concurrentFragments\":2.5")]
    public void Invalid_option_values_are_refused(string options)
    {
        Assert.Equal(NativeErrorCode.BadField, Refused(Download(options)).Code);
    }

    [Fact]
    public void A_missing_quality_is_refused()
    {
        Assert.Equal("options.quality", Refused(Download("\"subs\":true")).Field);
    }

    [Theory]
    [InlineData("1;2")]
    [InlineData("a")]
    [InlineData("1-")]
    [InlineData("1,,2")]
    [InlineData("1-5; rm")]
    public void Playlist_items_must_look_like_a_list_of_numbers_and_ranges(string items)
    {
        Assert.Equal("options.playlistItems", Refused(Download($"\"quality\":\"1080\",\"playlist\":true,\"playlistItems\":\"{items}\"")).Field);
    }

    [Fact]
    public void Playlist_items_need_the_playlist_flag()
    {
        Assert.Equal("options.playlistItems", Refused(Download("\"quality\":\"1080\",\"playlistItems\":\"1-3\"")).Field);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1:2:3:4")]
    [InlineData("-5")]
    [InlineData("1:30; rm")]
    public void A_trim_time_must_look_like_a_time(string time)
    {
        Assert.Equal("options.trim.start", Refused(Download($"\"quality\":\"1080\",\"trim\":{{\"start\":\"{time}\"}}")).Field);
    }

    [Fact]
    public void Custom_flags_go_with_the_custom_quality_and_nowhere_else()
    {
        Assert.Equal("options.custom", Refused(Download("\"quality\":\"custom\"")).Field);
        Assert.Equal("options.custom", Refused(Download("\"quality\":\"1080\",\"custom\":\"--exec calc\"")).Field);
        Assert.Equal("options.custom", Refused(Download("\"quality\":\"custom\",\"custom\":\"-f b\\n--exec calc\"")).Field);
    }

    [Fact]
    public void A_channel_job_needs_a_clean_archive_name_and_other_jobs_must_not_have_one()
    {
        Assert.Equal("options.archiveName", Refused(Download("\"quality\":\"1080\",\"job\":\"channel\"")).Field);
        Assert.Equal("options.archiveName", Refused(Download("\"quality\":\"1080\",\"job\":\"channel\",\"archiveName\":\"../../x\"")).Field);
        Assert.Equal("options.archiveName", Refused(Download("\"quality\":\"1080\",\"archiveName\":\"x\"")).Field);
        Assert.Equal("options.job", Refused(Download("\"quality\":\"1080\",\"job\":\"format\"")).Field);
    }

    // ---------------------------------------------------------------- auth

    [Fact]
    public void A_user_agent_must_be_printable_ascii()
    {
        Assert.Equal("auth.userAgent", Refused(Download(extra: ",\"auth\":{\"userAgent\":\"Mozilla \\u00E9\"}")).Field);
        Assert.Equal("auth.userAgent", Refused(Download(extra: ",\"auth\":{\"userAgent\":\"Mozilla\\r\\n--exec calc\"}")).Field);
        Assert.Equal("auth.userAgent", Refused(Download(extra: ",\"auth\":{\"userAgent\":\"" + new string('A', 513) + "\"}")).Field);
    }

    [Fact]
    public void Cookies_must_not_contain_nul_and_must_not_be_huge()
    {
        Assert.Equal("auth.cookies", Refused(Download(extra: ",\"auth\":{\"cookies\":\"a\\u0000b\"}")).Field);
        Assert.Equal("auth.cookies", Refused(Download(extra: ",\"auth\":{\"cookies\":\"" + new string('c', NativeProtocol.MaxCookieChars + 1) + "\"}")).Field);
    }

    [Fact]
    public void Error_messages_do_not_echo_long_or_control_character_input()
    {
        var error = Refused($"{{\"v\":1,\"id\":\"d1\",\"type\":\"hello\",\"{new string('x', 500)}\":1}}");

        Assert.True(error.Message.Length < 120, error.Message);
    }
}

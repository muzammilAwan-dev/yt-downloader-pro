using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

public class CommandInfoTests
{
    [Theory]
    [InlineData("YQ", "a")]                         // no padding
    [InlineData("Pz8/Pj4+", "???>>>")]              // standard alphabet
    [InlineData("Pz8_Pj4-", "???>>>")]              // URL-safe alphabet
    public void DecodeBase64_accepts_standard_and_url_safe_input(string input, string expected)
    {
        Assert.Equal(expected, CommandInfo.DecodeBase64(input));
    }

    [Theory]
    [InlineData("yt-dlp \"https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=5\"", "yt:dQw4w9WgXcQ")]
    [InlineData("yt-dlp \"https://youtu.be/dQw4w9WgXcQ\"", "yt:dQw4w9WgXcQ")]
    [InlineData("yt-dlp \"https://www.youtube.com/shorts/dQw4w9WgXcQ\"", "yt:dQw4w9WgXcQ")]
    [InlineData("yt-dlp \"https://www.youtube.com/embed/dQw4w9WgXcQ\"", "yt:dQw4w9WgXcQ")]
    public void Every_youtube_url_form_maps_to_the_same_key(string command, string expected)
    {
        Assert.Equal(expected, CommandInfo.ExtractVideoId(command));
    }

    [Fact]
    public void Other_sites_use_the_last_quoted_argument_as_a_stable_key()
    {
        const string cmd = "yt-dlp -o \"~/Downloads/%(title)s.mp4\" \"https://www.tiktok.com/@a/video/123\"";

        Assert.Equal("https://www.tiktok.com/@a/video/123", CommandInfo.ExtractVideoId(cmd));
        Assert.Equal(CommandInfo.ExtractVideoId(cmd), CommandInfo.ExtractVideoId(cmd));
    }

    [Fact]
    public void Without_any_quoted_argument_the_whole_command_is_the_key()
    {
        Assert.Equal("yt-dlp https://a.com", CommandInfo.ExtractVideoId("yt-dlp https://a.com"));
    }

    [Theory]
    [InlineData("yt-dlp -f \"bv*[height<=1080]+ba/b[height<=1080]\" \"https://a.com\"", "1080p")]
    [InlineData("yt-dlp -f \"best[height=720p]\" \"https://a.com\"", "720p")]
    [InlineData("yt-dlp -f \"ba/b\" -x --audio-format mp3 \"https://a.com\"", "Audio")]
    [InlineData("yt-dlp \"https://a.com\"", "")]
    public void Resolution_is_read_from_the_format_selector(string command, string expected)
    {
        Assert.Equal(expected, CommandInfo.ExtractResolution(command));
    }

    [Theory]
    [InlineData("yt-dlp -o \"~/Downloads/%(title)s_1080p.mp4\" \"https://a.com\"", "Fetching Title..._1080p")]
    [InlineData("yt-dlp -o \"~/Downloads/%(uploader)s.mp4\" \"https://a.com\"", "Channel")]
    [InlineData("yt-dlp \"https://a.com\"", "Fetching Title...")]
    public void Title_hint_comes_from_the_output_template(string command, string expected)
    {
        Assert.Equal(expected, CommandInfo.ExtractTitleHint(command));
    }
}

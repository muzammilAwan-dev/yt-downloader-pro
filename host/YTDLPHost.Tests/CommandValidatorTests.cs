using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>
/// Characterization tests for CommandValidator: they pin down what the validator does TODAY
/// (including its Windows-only path rules) so the Phase 1-3 refactors can't silently loosen it.
/// Commands are written with single quotes for readability; Q() turns them into double quotes.
/// </summary>
public class CommandValidatorTests
{
    private static string Q(string s) => s.Replace('\'', '"');

    private static bool Ok(string command, out string reason) => CommandValidator.TryValidate(Q(command), out reason);

    // ---------- accepted: shapes the extension really emits ----------

    [Theory]
    [InlineData("yt-dlp 'https://youtu.be/abc'")]
    [InlineData("yt-dlp.exe 'https://youtu.be/abc'")]
    [InlineData("YT-DLP 'https://youtu.be/abc'")]
    [InlineData("yt-dlp -f 'bv*[height<=1080]+ba/b[height<=1080]/bv*+ba/b' --merge-output-format mp4 -N 4 --no-playlist --restrict-filenames --embed-thumbnail --no-warnings --progress -o '~/Downloads/%(title)s_1080p.mp4' 'https://www.youtube.com/watch?v=abc'")]
    [InlineData(@"yt-dlp -f 'ba/b' -x --audio-format mp3 --audio-quality 0 -o 'C:\Users\me\Downloads\%(title)s_audio.mp3' 'https://youtu.be/abc'")]
    [InlineData("yt-dlp --download-sections '*00:01:15-00:01:25' --force-keyframes-at-cuts -o '~/Downloads/%(title)s_clip.mp4' 'https://youtu.be/abc'")]
    [InlineData("yt-dlp --write-subs --write-auto-subs --embed-subs --sub-langs 'en.*' --sleep-subtitles 5 'https://youtu.be/abc'")]
    [InlineData("yt-dlp --yes-playlist --playlist-items '1-5,8' -o '~/Downloads/%(playlist_title)s/%(playlist_index)03d_%(title)s.mp4' 'https://youtu.be/abc'")]
    [InlineData("yt-dlp --sponsorblock-remove all 'https://youtu.be/abc'")]
    [InlineData("yt-dlp -S 'vcodec:h264,res,acodec:m4a' 'https://youtu.be/abc'")]
    [InlineData("yt-dlp --download-archive '~/Downloads/archive.txt' 'https://youtu.be/abc'")]
    [InlineData("yt-dlp --limit-rate 5M -R 3 --retries 10 'https://youtu.be/a' 'https://youtu.be/b'")]
    public void Accepts_commands_the_extension_emits(string command)
    {
        Assert.True(Ok(command, out var reason), $"unexpectedly rejected: {reason}");
        Assert.Equal(string.Empty, reason);
    }

    // ---------- rejected: structure / injection ----------

    [Theory]
    [InlineData("", "empty command")]
    [InlineData("   ", "empty command")]
    [InlineData("yt-dlp 'https://a.com'\nyt-dlp --exec calc", "control characters")]
    [InlineData("yt-dlp 'https://a.com\\' --exec calc", "escaped quote")]
    [InlineData("yt-dlp 'https://a.com", "unbalanced quotes")]
    [InlineData("curl https://a.com", "command must start with yt-dlp")]
    [InlineData("yt-dlp 'https://a.com' -f", "missing value for -f")]
    [InlineData("yt-dlp -f --exec 'https://a.com'", "invalid value for -f")]
    [InlineData("yt-dlp -x", "no link in command")]
    [InlineData("yt-dlp file:///etc/passwd", "unexpected argument")]
    [InlineData("yt-dlp ftp://example.com/a", "unexpected argument")]
    [InlineData("yt-dlp 'javascript:alert(1)'", "unexpected argument")]
    public void Rejects_malformed_or_non_http_input(string command, string expectedReason)
    {
        Assert.False(Ok(command, out var reason));
        Assert.Contains(expectedReason, reason);
    }

    // ---------- rejected: flags that must never pass (the bypasses the validator's comments name) ----------

    [Theory]
    [InlineData("yt-dlp --exec calc 'https://a.com'", "--exec")]
    [InlineData("yt-dlp --external-downloader curl 'https://a.com'", "--external-downloader")]
    [InlineData("yt-dlp --ffmpeg-location x 'https://a.com'", "--ffmpeg-location")]
    [InlineData("yt-dlp --config-locations x 'https://a.com'", "--config-locations")]
    [InlineData("yt-dlp --output=evil.mp4 'https://a.com'", "--output=evil.mp4")]
    [InlineData("yt-dlp -P ~/x 'https://a.com'", "-P")]
    public void Rejects_flags_outside_the_allowlist(string command, string flag)
    {
        Assert.False(Ok(command, out var reason));
        Assert.Contains("option not allowed", reason);
        Assert.Contains(flag, reason);
    }

    // ---------- rejected: output paths ----------

    [Theory]
    [InlineData("yt-dlp -o '~/Downloads/x.exe' 'https://a.com'")]
    [InlineData("yt-dlp -o '~/Downloads/x.bat' 'https://a.com'")]
    [InlineData("yt-dlp -o '~/Downloads/x.ps1' 'https://a.com'")]
    [InlineData("yt-dlp -o '~/Downloads/noextension' 'https://a.com'")]
    [InlineData("yt-dlp -o '~/Downloads/../x.mp4' 'https://a.com'")]
    [InlineData(@"yt-dlp -o '\\server\share\x.mp4' 'https://a.com'")]
    [InlineData("yt-dlp -o 'downloads/x.mp4' 'https://a.com'")]
    [InlineData(@"yt-dlp -o 'C:\Windows\x.mp4' 'https://a.com'")]
    [InlineData(@"yt-dlp -o 'C:\Program Files\x.mp4' 'https://a.com'")]
    [InlineData(@"yt-dlp -o 'C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\x.mp4' 'https://a.com'")]
    [InlineData(@"yt-dlp -o 'C:\Users\me\AppData\Local\YTDownloaderProEngine\x.mp4' 'https://a.com'")]
    public void Rejects_unsafe_output_paths(string command)
    {
        Assert.False(Ok(command, out var reason));
        Assert.Contains("output path or file type not allowed", reason);
    }

    [Fact]
    public void Download_archive_must_be_a_txt_file()
    {
        Assert.False(Ok("yt-dlp --download-archive '~/a.mp4' 'https://a.com'", out var reason));
        Assert.Contains("archive path not allowed", reason);
    }

    // ---------- documents a known gap that Phase 3 must close ----------

    [Fact]
    public void KnownGap_UnixAbsolutePaths_AreRejectedToday()
    {
        // The validator only understands "C:\..." and "~". On macOS/Linux every "/home/..." output
        // path is rejected. Phase 3 replaces this with allowed-roots validation; when it lands,
        // flip this assertion (and add the symlink-escape and traversal cases per OS).
        Assert.False(Ok("yt-dlp -o '/home/me/Downloads/%(title)s.mp4' 'https://a.com'", out var reason));
        Assert.Contains("output path or file type not allowed", reason);
    }
}

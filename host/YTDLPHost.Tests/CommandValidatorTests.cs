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
    [InlineData("yt-dlp 'https://youtu.be/abc' --user-agent 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'")]
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

    [Fact]
    public void A_user_agent_value_cannot_carry_a_second_flag()
    {
        Assert.False(Ok("yt-dlp 'https://a.com' --user-agent 'x' --exec 'calc'", out var reason));
        Assert.Contains("option not allowed", reason);
    }

    // ---------- Unix absolute output paths ----------

    [Theory]
    [InlineData("yt-dlp -o '/home/me/Downloads/%(title)s.mp4' 'https://a.com'")]
    [InlineData("yt-dlp -o '/Users/me/Movies/%(title)s.mp4' 'https://a.com'")]
    [InlineData("yt-dlp -o '/mnt/data/videos/%(title)s.mkv' 'https://a.com'")]
    [InlineData("yt-dlp --download-archive '/home/me/archive.txt' 'https://a.com'")]
    public void Accepts_unix_absolute_output_paths(string command)
    {
        Assert.True(Ok(command, out var reason), reason);
    }

    [Theory]
    [InlineData("yt-dlp -o '/etc/x.mp4' 'https://a.com'")]
    [InlineData("yt-dlp -o '/usr/local/x.mp4' 'https://a.com'")]
    [InlineData("yt-dlp -o '/bin/x.mp4' 'https://a.com'")]
    [InlineData("yt-dlp -o '/root/x.mp4' 'https://a.com'")]
    [InlineData("yt-dlp -o '/System/x.mp4' 'https://a.com'")]
    [InlineData("yt-dlp -o '/home/me/../../etc/x.mp4' 'https://a.com'")]
    [InlineData("yt-dlp -o '/home/me/.ssh/x.mp4' 'https://a.com'")]
    [InlineData("yt-dlp -o '/home/me/noextension' 'https://a.com'")]
    [InlineData("yt-dlp -o '/home/me/x.sh' 'https://a.com'")]
    [InlineData("yt-dlp -o '//server/share/x.mp4' 'https://a.com'")]
    public void Rejects_unsafe_unix_output_paths(string command)
    {
        Assert.False(Ok(command, out _));
    }

    // ---------- path policy (step 3.2) ----------

    [Theory]
    [InlineData(@"yt-dlp -o 'C:\Windows.\a.mp4' 'https://a.com'")]
    [InlineData(@"yt-dlp -o 'C:\Windows \a.mp4' 'https://a.com'")]
    [InlineData(@"yt-dlp -o 'C:\PROGRA~1\a.mp4' 'https://a.com'")]
    [InlineData(@"yt-dlp -o 'C:\Users\me\.\..\a.mp4' 'https://a.com'")]
    [InlineData("yt-dlp -o '~other/a.mp4' 'https://a.com'")]
    public void Rejects_path_tricks_that_a_text_blocklist_would_miss(string command)
    {
        Assert.False(Ok(command, out _));
    }

    [Fact]
    public void Rejects_an_output_folder_that_is_a_link_into_a_system_folder()
    {
        var policy = new PathPolicy("/home/me", path => path == "/home/me/Videos/link" ? "/etc" : null);

        Assert.False(CommandValidator.TryValidate(Q("yt-dlp -o '/home/me/Videos/link/%(title)s.mp4' 'https://a.com'"), policy, out _));
    }

    [Fact]
    public void Accepts_the_same_command_when_the_link_leads_somewhere_harmless()
    {
        var policy = new PathPolicy("/home/me", path => path == "/home/me/Videos/link" ? "/mnt/media" : null);

        Assert.True(CommandValidator.TryValidate(Q("yt-dlp -o '/home/me/Videos/link/%(title)s.mp4' 'https://a.com'"), policy, out var reason), reason);
    }

    [Fact]
    public void Expands_the_tilde_with_the_policy_home_and_still_wants_a_media_extension()
    {
        var policy = new PathPolicy("/home/me", _ => null);

        Assert.True(CommandValidator.TryValidate(Q("yt-dlp -o '~/Downloads/%(title)s.mp4' 'https://a.com'"), policy, out var reason), reason);
        Assert.False(CommandValidator.TryValidate(Q("yt-dlp -o '~/Downloads/%(title)s.sh' 'https://a.com'"), policy, out _));
    }
}

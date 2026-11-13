using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Phase 3, step 3.2: the path rules for ytdlp:// output paths. Text work only (links come from a fake), so both path styles run on every OS.</summary>
public class PathPolicyTests
{
    private static PathPolicy Policy(string home = "/home/me", params (string From, string To)[] links)
    {
        var map = links.ToDictionary(l => l.From, l => l.To, StringComparer.OrdinalIgnoreCase);
        return new PathPolicy(home, path => map.TryGetValue(path, out var target) ? target : null);
    }

    [Theory]
    [InlineData(@"/home/me/Videos/a.mp4")]
    [InlineData(@"/Users/me/Movies/%(title)s.mp4")]
    [InlineData(@"/mnt/data/clips/a.mkv")]
    [InlineData(@"/home/me/Videos/%(upload_date>%H:%M:%S)s.mp4")]
    [InlineData(@"/home/me/windows-games/a.mp4")]
    [InlineData(@"~/Downloads/%(title)s.mp4")]
    [InlineData(@"~\Downloads\a.mp4")]
    [InlineData(@"C:\Users\me\Videos\%(title)s.mp4")]
    [InlineData(@"D:/clips/a.mp4")]
    [InlineData(@"C:\\Users\\me//Videos\a.mp4")]
    [InlineData(@"C:\Users\me\.\Videos\a.mp4")]
    public void Accepts_ordinary_absolute_paths_of_both_styles(string path)
    {
        Assert.True(Policy().IsAllowed(path), path);
    }

    [Theory]
    [InlineData(@"")]
    [InlineData(@"   ")]
    [InlineData(@"a.mp4")]
    [InlineData(@"./a.mp4")]
    [InlineData(@"Videos\a.mp4")]
    [InlineData(@"C:a.mp4")]
    [InlineData(@"C:\")]
    [InlineData(@"/")]
    [InlineData(@"~other/a.mp4")]
    [InlineData(@"\\server\share\a.mp4")]
    [InlineData(@"//server/share/a.mp4")]
    [InlineData(@"\\?\C:\Users\me\a.mp4")]
    [InlineData(@"C:\Users\me\..\Windows\a.mp4")]
    [InlineData(@"/home/me/../../etc/a.mp4")]
    [InlineData(@"/home/me/Videos/..")]
    [InlineData(@"C:\Windows.\a.mp4")]
    [InlineData(@"C:\Windows \a.mp4")]
    [InlineData(@"C:\Windows...\a.mp4")]
    [InlineData(@"C:\PROGRA~1\a.mp4")]
    [InlineData(@"C:\Users\me\a.mp4:stream")]
    [InlineData(@"C:\Users\me\a:b\c.mp4")]
    [InlineData(@"C:\Windows\a.mp4")]
    [InlineData(@"C:\WINDOWS\System32\a.mp4")]
    [InlineData(@"D:\Program Files (x86)\x\a.mp4")]
    [InlineData(@"C:\ProgramData\a.mp4")]
    [InlineData(@"C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\a.mp4")]
    [InlineData(@"C:\Users\me\AppData\Local\YTDownloaderProEngine\a.mp4")]
    [InlineData(@"/etc/a.mp4")]
    [InlineData(@"/ETC/a.mp4")]
    [InlineData(@"/etc//a.mp4")]
    [InlineData(@"/usr/local/a.mp4")]
    [InlineData(@"/bin/a.mp4")]
    [InlineData(@"/lib64/a.mp4")]
    [InlineData(@"/root/a.mp4")]
    [InlineData(@"/proc/1/a.mp4")]
    [InlineData(@"/Library/a.mp4")]
    [InlineData(@"/System/a.mp4")]
    [InlineData(@"/private/etc/a.mp4")]
    [InlineData(@"/Applications/a.mp4")]
    [InlineData(@"/mnt/c/Windows/System32/a.mp4")]
    [InlineData(@"/home/me/.ssh/a.mp4")]
    [InlineData(@"/home/me/.gnupg/a.mp4")]
    [InlineData(@"/home/me/.local/share/yt-downloader-pro/a.mp4")]
    [InlineData(@"/home/me/.config/yt-downloader-pro/a.mp4")]
    [InlineData(@"/Users/me/Library/Application Support/YT Downloader Pro/a.mp4")]
    [InlineData(@"/Users/me/Library/Logs/YT Downloader Pro/a.mp4")]
    [InlineData(@"~/.ssh/a.mp4")]
    public void Refuses_relative_odd_traversing_and_system_paths(string path)
    {
        Assert.False(Policy().IsAllowed(path), path);
    }

    [Fact]
    public void A_tilde_means_the_home_folder_even_when_it_is_a_Windows_one()
    {
        var policy = Policy(@"C:\Users\me");

        Assert.True(policy.IsAllowed("~/Downloads/a.mp4"));
        Assert.False(policy.IsAllowed("~/AppData/Roaming/Microsoft/a.mp4"));
    }

    [Fact]
    public void A_link_that_leads_into_a_system_folder_is_refused()
    {
        var policy = Policy("/home/me", ("/home/me/Videos/link", "/etc"));

        Assert.False(policy.IsAllowed("/home/me/Videos/link/a.mp4"));
    }

    [Fact]
    public void A_chain_of_links_is_followed_to_the_end()
    {
        var policy = Policy("/home/me", ("/home/me/a", "/home/me/b"), ("/home/me/b", "/usr/lib"));

        Assert.False(policy.IsAllowed("/home/me/a/x.mp4"));
    }

    [Fact]
    public void A_link_to_a_harmless_folder_is_fine()
    {
        var policy = Policy("/home/me", ("/home/me/Videos", "/mnt/media/videos"));

        Assert.True(policy.IsAllowed("/home/me/Videos/a.mp4"));
    }

    [Fact]
    public void A_file_that_is_itself_a_link_into_the_ssh_folder_is_refused()
    {
        var policy = Policy("/home/me", ("/home/me/a.mp4", "/home/me/.ssh/authorized_keys"));

        Assert.False(policy.IsAllowed("/home/me/a.mp4"));
    }

    [Fact]
    public void Links_that_go_round_in_a_circle_are_refused()
    {
        var policy = Policy("/home/me", ("/home/me/a", "/home/me/b"), ("/home/me/b", "/home/me/a"));

        Assert.False(policy.IsAllowed("/home/me/a/x.mp4"));
    }

    [Fact]
    public void A_link_whose_target_is_not_an_absolute_path_is_refused()
    {
        var policy = Policy("/home/me", ("/home/me/a", "relative/dir"));

        Assert.False(policy.IsAllowed("/home/me/a/x.mp4"));
    }

    [Fact]
    public void A_Windows_junction_into_the_system_folder_is_refused()
    {
        var policy = Policy(@"C:\Users\me", (@"C:\Users\me\Videos", @"C:\Windows\System32"));

        Assert.False(policy.IsAllowed(@"C:\Users\me\Videos\a.mp4"));
    }
}

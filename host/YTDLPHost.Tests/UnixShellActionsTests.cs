using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Step 3.6: "show in folder", "open folder" and "play" on macOS and Linux. The commands are plain data and the runner is a fake, so this runs on every OS.</summary>
public class UnixShellActionsTests
{
    private static string Line(ShellCommand c) => c.FileName + " " + string.Join(" ", c.Arguments);

    [Fact]
    public void On_macOS_reveal_uses_open_dash_R_and_the_others_use_plain_open()
    {
        Assert.Equal(new[] { "open -R /Users/u/Movies/a.mp4" }, UnixShellCommands.Reveal(HostOs.MacOS, "/Users/u/Movies/a.mp4").Select(Line));
        Assert.Equal(new[] { "open /Users/u/Movies" }, UnixShellCommands.OpenFolder(HostOs.MacOS, "/Users/u/Movies").Select(Line));
        Assert.Equal(new[] { "open /Users/u/Movies/a.mp4" }, UnixShellCommands.OpenFile(HostOs.MacOS, "/Users/u/Movies/a.mp4").Select(Line));
    }

    [Fact]
    public void On_Linux_reveal_asks_the_file_manager_over_dbus_and_falls_back_to_opening_the_folder()
    {
        var commands = UnixShellCommands.Reveal(HostOs.Linux, "/home/u/My Videos/a b,c.mp4");

        Assert.Equal(2, commands.Count);
        Assert.Equal("dbus-send", commands[0].FileName);
        Assert.Contains("org.freedesktop.FileManager1.ShowItems", commands[0].Arguments);
        Assert.Contains("array:string:file:///home/u/My%20Videos/a%20b%2Cc.mp4", commands[0].Arguments);   // one argument: spaces and commas are escaped
        Assert.Equal("xdg-open /home/u/My Videos", Line(commands[1]));
    }

    [Fact]
    public void On_Linux_folders_and_files_open_with_xdg_open()
    {
        Assert.Equal(new[] { "xdg-open /home/u/Downloads" }, UnixShellCommands.OpenFolder(HostOs.Linux, "/home/u/Downloads").Select(Line));
        Assert.Equal(new[] { "xdg-open /home/u/Downloads/a.mp4" }, UnixShellCommands.OpenFile(HostOs.Linux, "/home/u/Downloads/a.mp4").Select(Line));
    }

    [Fact]
    public void Windows_has_its_own_implementation_so_this_one_has_no_commands()
    {
        Assert.Empty(UnixShellCommands.Reveal(HostOs.Windows, "/x/a.mp4"));
        Assert.Empty(UnixShellCommands.OpenFolder(HostOs.Windows, "/x"));
    }

    [Fact]
    public void The_first_way_that_works_is_the_only_one_used()
    {
        var run = new List<string>();
        var shell = new UnixShellActions(HostOs.Linux, c => { run.Add(c.FileName); return true; });

        shell.RevealFile("/home/u/a.mp4");

        Assert.Equal(new[] { "dbus-send" }, run);
    }

    [Fact]
    public void A_way_that_fails_or_cannot_start_is_followed_by_the_next_and_nothing_is_thrown()
    {
        var run = new List<string>();
        var shell = new UnixShellActions(HostOs.Linux, c =>
        {
            run.Add(c.FileName);
            if (c.FileName == "dbus-send") throw new System.ComponentModel.Win32Exception("not installed");
            return false;
        });

        shell.RevealFile("/home/u/a.mp4");   // must not throw even when every way fails

        Assert.Equal(new[] { "dbus-send", "xdg-open" }, run);
    }

    [Fact]
    public void A_path_that_is_not_absolute_is_refused_before_any_program_starts()
    {
        var run = new List<string>();
        var shell = new UnixShellActions(HostOs.MacOS, c => { run.Add(c.FileName); return true; });

        shell.OpenFile("-a.mp4");
        shell.RevealFile("relative/a.mp4");
        shell.OpenFolder("");

        Assert.Empty(run);
    }
}

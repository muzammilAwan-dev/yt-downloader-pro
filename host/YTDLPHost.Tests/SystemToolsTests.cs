using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Step 3.5b: finding ffmpeg and ffprobe on macOS and Linux. Pure text work on a fake file system, so it runs the same on every OS.</summary>
public class SystemToolsTests
{
    private static ToolSearch Search(HostOs os, string? path, params string[] existing) =>
        new(os, path, new HashSet<string>(existing).Contains);

    [Fact]
    public void A_tool_is_found_in_the_first_PATH_folder_that_has_it()
    {
        var search = Search(HostOs.Linux, "/opt/none:/usr/bin", "/usr/bin/ffmpeg");

        Assert.Equal("/usr/bin/ffmpeg", SystemTools.Find("ffmpeg", search));
        Assert.Equal("/usr/bin", SystemTools.FindFolder("ffmpeg", search));
    }

    [Fact]
    public void An_earlier_PATH_folder_wins()
    {
        var search = Search(HostOs.Linux, "/opt/custom:/usr/bin", "/usr/bin/ffmpeg", "/opt/custom/ffmpeg");

        Assert.Equal("/opt/custom/ffmpeg", SystemTools.Find("ffmpeg", search));
    }

    [Fact]
    public void Empty_and_relative_PATH_entries_are_ignored_so_nothing_runs_from_the_current_folder()
    {
        var search = Search(HostOs.Linux, ":.:bin:/usr/bin", "./ffmpeg", "bin/ffmpeg", "/ffmpeg", "/usr/bin/ffmpeg");

        Assert.Equal("/usr/bin/ffmpeg", SystemTools.Find("ffmpeg", search));
        Assert.Null(SystemTools.Find("ffmpeg", Search(HostOs.Linux, ":.:bin", "./ffmpeg", "bin/ffmpeg")));
    }

    [Fact]
    public void On_macOS_the_Homebrew_folder_is_searched_even_when_PATH_does_not_list_it()
    {
        // an app started from Finder gets a short PATH without /opt/homebrew/bin
        var search = Search(HostOs.MacOS, "/usr/bin:/bin:/usr/sbin:/sbin", "/opt/homebrew/bin/ffmpeg");

        Assert.Equal("/opt/homebrew/bin/ffmpeg", SystemTools.Find("ffmpeg", search));
    }

    [Fact]
    public void On_Windows_PATH_is_split_at_semicolons_and_the_file_gets_exe()
    {
        var search = Search(HostOs.Windows, @"C:\tools;""C:\ffmpeg\bin""", @"C:\ffmpeg\bin\ffmpeg.exe");

        Assert.Equal(@"C:\ffmpeg\bin\ffmpeg.exe", SystemTools.Find("ffmpeg", search));
    }

    [Fact]
    public void A_missing_tool_gives_null()
    {
        Assert.Null(SystemTools.Find("ffmpeg", Search(HostOs.Linux, "/usr/bin")));
        Assert.Null(SystemTools.Find("ffmpeg", Search(HostOs.MacOS, null)));
        Assert.Null(SystemTools.Find("ffmpeg", Search(HostOs.Windows, null)));
    }
}

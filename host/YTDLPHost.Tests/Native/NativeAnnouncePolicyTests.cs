using System.Text.Json;
using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Native;

namespace YTDLPHost.Tests.Native;

/// <summary>What the app does so the user notices a download that came from the browser.</summary>
public class NativeAnnouncePolicyTests
{
    [Theory]
    [InlineData(0, false, false, NativeAnnouncement.None)]   // a repeat of a running download adds nothing
    [InlineData(0, false, true, NativeAnnouncement.None)]
    [InlineData(1, true, false, NativeAnnouncement.None)]    // the window is on screen and shows the new row
    [InlineData(1, true, true, NativeAnnouncement.None)]
    [InlineData(1, false, false, NativeAnnouncement.Notify)] // hidden in the tray: a short notification (the default)
    [InlineData(5, false, false, NativeAnnouncement.Notify)]
    [InlineData(1, false, true, NativeAnnouncement.ShowWindow)] // the user chose "show the window"
    public void The_announcement_depends_on_what_was_added_the_window_and_the_setting(int added, bool windowVisible, bool showSetting, NativeAnnouncement expected)
    {
        Assert.Equal(expected, NativeAnnouncePolicy.Decide(added, windowVisible, showSetting));
    }

    [Fact]
    public void The_window_is_not_forced_forward_by_default_and_old_settings_files_keep_that()
    {
        Assert.False(new AppSettings().ShowWindowOnNativeDownload);
        Assert.False(JsonSerializer.Deserialize<AppSettings>("{\"SpeedLimit\":\"0\"}")!.ShowWindowOnNativeDownload);
    }

    [Fact]
    public void The_setting_survives_the_settings_file()
    {
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { ShowWindowOnNativeDownload = true }));

        Assert.True(loaded!.ShowWindowOnNativeDownload);
    }
}

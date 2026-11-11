using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>The "Browser downloads folder" setting: only a folder the browser-download code can really use may be saved.</summary>
public class SettingsRulesFolderTests
{
    [Theory]
    [InlineData("", "")]                                  // empty = the app's default folder
    [InlineData("   ", "")]
    [InlineData("D:\\Videos", "D:\\Videos")]
    [InlineData("  D:/Videos/YT  ", "D:/Videos/YT")]      // trimmed, otherwise kept as typed
    [InlineData("~/Videos", "~/Videos")]
    [InlineData("/home/user/Videos", "/home/user/Videos")]
    public void A_usable_folder_is_accepted_and_kept(string typed, string expectedStored)
    {
        Assert.True(SettingsRules.TryNormalizeDownloadFolder(typed, out var stored, out var error));
        Assert.Equal(expectedStored, stored);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData("videos", "full folder path")]            // relative: it would depend on the working folder
    [InlineData("Downloads\\YT", "full folder path")]
    [InlineData("D:\\Vid\"eos", "cannot be used")]        // a quote would end the argument
    [InlineData("D:\\Vid\u0007eos", "cannot be used")]    // control character
    public void An_unusable_folder_is_refused_with_a_reason(string typed, string expectedInError)
    {
        Assert.False(SettingsRules.TryNormalizeDownloadFolder(typed, out var stored, out var error));
        Assert.Equal("", stored);
        Assert.Contains(expectedInError, error);
    }
}

using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Step 4.1: the "--hidden" switch of the Avalonia app (start in the tray without a window).</summary>
public class StartHiddenTests
{
    [Fact]
    public void The_hidden_switch_is_found_anywhere_in_the_arguments_in_any_letter_case()
    {
        Assert.True(HostCommandLine.StartHidden(new[] { "--hidden" }));
        Assert.True(HostCommandLine.StartHidden(new[] { "--HIDDEN" }));
        Assert.True(HostCommandLine.StartHidden(new[] { "ytdlp://x", "--hidden" }));
    }

    [Fact]
    public void Anything_else_does_not_hide_the_window()
    {
        Assert.False(HostCommandLine.StartHidden(Array.Empty<string>()));
        Assert.False(HostCommandLine.StartHidden(new[] { "hidden" }));
        Assert.False(HostCommandLine.StartHidden(new[] { "--hiddenly" }));
        Assert.False(HostCommandLine.StartHidden(new[] { "ytdlp://hidden" }));
    }
}

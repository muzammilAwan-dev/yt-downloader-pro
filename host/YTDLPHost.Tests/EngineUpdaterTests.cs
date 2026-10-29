using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>What the "yt-dlp --update-to" output means, and the text the user sees (the update itself needs a real yt-dlp and is checked by hand).</summary>
public class EngineUpdaterTests
{
    [Fact]
    public void The_updated_line_means_updated_and_names_the_new_version()
    {
        var output = "Current version: nightly@2026.10.01 from yt-dlp/yt-dlp-nightly-builds\nLatest version: nightly@2026.10.28\nUpdating to nightly@2026.10.28 ...\nUpdated yt-dlp to nightly@2026.10.28 from yt-dlp/yt-dlp-nightly-builds";

        var result = EngineUpdater.Interpret(0, false, output, "2026.10.01.123456", "2026.10.28.010101");

        Assert.Equal(EngineUpdateStatus.Updated, result.Status);
        Assert.Contains("2026.10.28.010101", result.Message);
    }

    [Fact]
    public void A_changed_version_alone_counts_as_updated()
    {
        var result = EngineUpdater.Interpret(0, false, "", "2026.10.01", "2026.10.28");

        Assert.Equal(EngineUpdateStatus.Updated, result.Status);
    }

    [Fact]
    public void Up_to_date_output_with_the_same_version_is_up_to_date()
    {
        var result = EngineUpdater.Interpret(0, false, "yt-dlp is up to date (nightly@2026.10.28)", "2026.10.28", "2026.10.28");

        Assert.Equal(EngineUpdateStatus.UpToDate, result.Status);
    }

    [Theory]
    [InlineData(1, "ERROR: Unable to obtain version info (<urlopen error [Errno 11001] getaddrinfo failed>)", "internet connection")]
    [InlineData(1, "ERROR: Unable to write to C:\\engine\\yt-dlp.exe: Permission denied", "Permission denied")]
    [InlineData(1, "", "exit code 1")]
    [InlineData(0, "ERROR: something unexpected", "something unexpected")]
    public void Errors_are_failures_with_a_readable_reason(int exitCode, string output, string expectedInMessage)
    {
        var result = EngineUpdater.Interpret(exitCode, false, output, "2026.10.01", "2026.10.01");

        Assert.Equal(EngineUpdateStatus.Failed, result.Status);
        Assert.Contains(expectedInMessage, result.Message);
    }

    [Fact]
    public void A_timeout_is_a_failure()
    {
        var result = EngineUpdater.Interpret(-1, true, "", "2026.10.01", "2026.10.01");

        Assert.Equal(EngineUpdateStatus.Failed, result.Status);
        Assert.Contains("did not finish", result.Message);
    }

    [Fact]
    public void The_updated_line_wins_over_warning_and_error_lines_around_it()
    {
        var output = "WARNING: something minor\nERROR: [debug] noise\nUpdated yt-dlp to nightly@2026.10.28 from yt-dlp/yt-dlp-nightly-builds";

        var result = EngineUpdater.Interpret(0, false, output, "2026.10.01", "2026.10.28");

        Assert.Equal(EngineUpdateStatus.Updated, result.Status);
    }

    [Fact]
    public void A_very_long_error_line_is_cut()
    {
        var result = EngineUpdater.Interpret(1, false, "ERROR: " + new string('x', 500), null, null);

        Assert.Equal(EngineUpdateStatus.Failed, result.Status);
        Assert.True(result.Message.Length <= "The update failed: ".Length + 200);
    }

    [Fact]
    public void The_dialog_text_of_an_update_shows_both_versions_and_the_tip()
    {
        var text = new EngineUpdateResult(EngineUpdateStatus.Updated, "2026.10.01", "2026.10.28", "The engine was updated to 2026.10.28.").ToDialogText();

        Assert.Contains("Previous version: 2026.10.01", text);
        Assert.Contains("Current version: 2026.10.28", text);
        Assert.Contains(EngineUpdater.TroubleshootingHint, text);
    }

    [Fact]
    public void The_dialog_text_of_an_up_to_date_engine_shows_the_current_version()
    {
        var text = new EngineUpdateResult(EngineUpdateStatus.UpToDate, "2026.10.28", "2026.10.28", "The engine is already up to date.").ToDialogText();

        Assert.Contains("Current version: 2026.10.28", text);
        Assert.DoesNotContain("Previous version", text);
    }

    [Fact]
    public void The_dialog_text_of_a_failure_has_no_version_lines_but_the_tip()
    {
        var text = new EngineUpdateResult(EngineUpdateStatus.Failed, "2026.10.01", "2026.10.01", "The update failed (exit code 1).").ToDialogText();

        Assert.DoesNotContain("version:", text);
        Assert.Contains(EngineUpdater.TroubleshootingHint, text);
    }
}

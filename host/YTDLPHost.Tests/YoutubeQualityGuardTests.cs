using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

public class YoutubeQualityGuardTests
{
    private static string Pick(string ids) => $"[info] abcdefghijk: Downloading 1 format(s): {ids}";

    [Theory]
    [InlineData("18", 1080, "only 360p offered by YouTube (wanted 1080p)")]
    [InlineData("22", 1080, "only 720p offered by YouTube (wanted 1080p)")]
    [InlineData("17", 480, "only 144p offered by YouTube (wanted 480p)")]
    public void A_legacy_format_below_the_requested_height_is_reported(string formatId, int requested, string expectedNote)
    {
        var guard = new YoutubeQualityGuard(isYoutube: true, requestedHeight: requested);

        var warning = guard.Observe(Pick(formatId));

        Assert.Equal(expectedNote, guard.Note);
        Assert.NotNull(warning);
        Assert.Contains($"legacy format {formatId}", warning);
    }

    [Fact]
    public void A_legacy_format_that_meets_the_request_is_fine()
    {
        var guard = new YoutubeQualityGuard(true, 360);

        Assert.Null(guard.Observe(Pick("18")));
        Assert.Equal("", guard.Note);
    }

    [Theory]
    [InlineData("137+140")]
    [InlineData("248+251")]
    public void Normal_adaptive_picks_are_not_reported(string ids)
    {
        var guard = new YoutubeQualityGuard(true, 1080);

        Assert.Null(guard.Observe(Pick(ids)));
        Assert.Equal("", guard.Note);
    }

    [Fact]
    public void Other_sites_and_unknown_heights_are_never_reported()
    {
        Assert.Null(new YoutubeQualityGuard(isYoutube: false, requestedHeight: 1080).Observe(Pick("18")));
        Assert.Null(new YoutubeQualityGuard(isYoutube: true, requestedHeight: 0).Observe(Pick("18")));
        Assert.Null(YoutubeQualityGuard.Disabled.Observe(Pick("18")));
    }

    [Fact]
    public void The_guard_reports_only_once()
    {
        var guard = new YoutubeQualityGuard(true, 1080);

        Assert.NotNull(guard.Observe(Pick("18")));
        Assert.Null(guard.Observe(Pick("22")));
        Assert.Equal("only 360p offered by YouTube (wanted 1080p)", guard.Note);
    }

    [Fact]
    public void Unrelated_lines_are_ignored()
    {
        var guard = new YoutubeQualityGuard(true, 1080);

        Assert.Null(guard.Observe("[download]  45.2% of 100.00MiB at 2.00MiB/s ETA 00:30"));
    }
}

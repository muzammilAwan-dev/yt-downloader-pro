using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

public class SettingsRulesTests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData(" 500k ", "500K")]
    [InlineData("2.5M", "2.5M")]
    [InlineData("10g", "10G")]
    [InlineData(null, "0")]
    public void Valid_speed_limits_are_normalized(string? raw, string expected)
    {
        Assert.True(SettingsRules.TryNormalizeSpeedLimit(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("abc")]
    [InlineData("5MB")]
    [InlineData("-5M")]
    [InlineData("1.M")]
    [InlineData("5 M")]
    [InlineData("5M --exec calc")]
    public void Invalid_speed_limits_are_rejected(string raw)
    {
        Assert.False(SettingsRules.TryNormalizeSpeedLimit(raw, out _));
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    [InlineData(15, 15)]
    [InlineData(99, 15)]
    public void Concurrent_downloads_are_kept_between_1_and_15(int value, int expected)
    {
        Assert.Equal(expected, SettingsRules.ClampConcurrentDownloads(value));
    }
}

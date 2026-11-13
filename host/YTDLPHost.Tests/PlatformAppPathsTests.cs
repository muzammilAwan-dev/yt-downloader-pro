using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Phase 3, step 3.1: the folder layout per OS. The factory takes the OS, the environment and the home folder, so every layout runs on every OS.</summary>
public class PlatformAppPathsTests
{
    private static readonly string Home = Path.Combine(Path.GetTempPath(), "home-of-test");
    private static readonly string Tmp = Path.Combine(Path.GetTempPath(), "tmp-of-test");

    private static Func<string, string?> Env(params (string Key, string Value)[] vars)
    {
        var map = vars.ToDictionary(v => v.Key, v => v.Value);
        return key => map.TryGetValue(key, out var value) ? value : null;
    }

    [Fact]
    public void Windows_keeps_the_legacy_layout()
    {
        Assert.IsType<LegacyAppPaths>(AppPathsFactory.Create(HostOs.Windows, Env(), Home, Tmp));
    }

    [Fact]
    public void The_default_is_the_legacy_layout_on_Windows_and_something_else_everywhere_else()
    {
        Assert.Equal(AppPathsFactory.CurrentOs() == HostOs.Windows, AppPathsFactory.CreateDefault() is LegacyAppPaths);
    }

    [Fact]
    public void MacOS_uses_Application_Support_and_Library_Logs()
    {
        var p = AppPathsFactory.Create(HostOs.MacOS, Env(), Home, Tmp);
        string support = Path.Combine(Home, "Library", "Application Support", "YT Downloader Pro");

        Assert.Equal(Home, p.HomeDir);
        Assert.Equal(Path.Combine(Home, "Downloads"), p.DownloadsDir);
        Assert.Equal(support, p.DataDir);
        Assert.Equal(support, p.ConfigDir);
        Assert.Equal(Path.Combine(support, "engine"), p.EngineDir);
        Assert.Equal(Path.Combine(support, "Payloads"), p.PayloadsDir);
        Assert.Equal(Path.Combine(Home, "Library", "Logs", "YT Downloader Pro"), p.LogsDir);
        Assert.Equal(Tmp, p.TempDir);
    }

    [Fact]
    public void Linux_without_XDG_variables_uses_the_spec_defaults_under_home()
    {
        var p = AppPathsFactory.Create(HostOs.Linux, Env(), Home, Tmp);

        Assert.Equal(Home, p.HomeDir);
        Assert.Equal(Path.Combine(Home, "Downloads"), p.DownloadsDir);
        Assert.Equal(Path.Combine(Home, ".local", "share", "yt-downloader-pro"), p.DataDir);
        Assert.Equal(Path.Combine(Home, ".local", "share", "yt-downloader-pro", "engine"), p.EngineDir);
        Assert.Equal(Path.Combine(Home, ".config", "yt-downloader-pro"), p.ConfigDir);
        Assert.Equal(Path.Combine(Home, ".local", "state", "yt-downloader-pro", "logs"), p.LogsDir);
        Assert.Equal(Path.Combine(Home, ".local", "state", "yt-downloader-pro", "payloads"), p.PayloadsDir);
        Assert.Equal(Tmp, p.TempDir);
    }

    [Fact]
    public void Linux_honors_absolute_XDG_variables()
    {
        string data = Path.Combine(Path.GetTempPath(), "xdg-data"), config = Path.Combine(Path.GetTempPath(), "xdg-config"), state = Path.Combine(Path.GetTempPath(), "xdg-state");
        var p = AppPathsFactory.Create(HostOs.Linux, Env(("XDG_DATA_HOME", data), ("XDG_CONFIG_HOME", config), ("XDG_STATE_HOME", state)), Home, Tmp);

        Assert.Equal(Path.Combine(data, "yt-downloader-pro"), p.DataDir);
        Assert.Equal(Path.Combine(data, "yt-downloader-pro", "engine"), p.EngineDir);
        Assert.Equal(Path.Combine(config, "yt-downloader-pro"), p.ConfigDir);
        Assert.Equal(Path.Combine(state, "yt-downloader-pro", "logs"), p.LogsDir);
        Assert.Equal(Path.Combine(state, "yt-downloader-pro", "payloads"), p.PayloadsDir);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/dir")]
    public void Linux_ignores_XDG_variables_that_are_empty_or_not_absolute(string value)
    {
        var p = AppPathsFactory.Create(HostOs.Linux, Env(("XDG_DATA_HOME", value), ("XDG_CONFIG_HOME", value), ("XDG_STATE_HOME", value)), Home, Tmp);

        Assert.Equal(Path.Combine(Home, ".local", "share", "yt-downloader-pro"), p.DataDir);
        Assert.Equal(Path.Combine(Home, ".config", "yt-downloader-pro"), p.ConfigDir);
        Assert.Equal(Path.Combine(Home, ".local", "state", "yt-downloader-pro", "logs"), p.LogsDir);
    }

    [Fact]
    public void Settings_stay_next_to_the_data_except_on_Linux()
    {
        IAppPaths legacy = new LegacyAppPaths();   // typed as the interface: ConfigDir is a default interface member, invisible on the class type
        Assert.Equal(legacy.DataDir, legacy.ConfigDir);

        var mac = AppPathsFactory.Create(HostOs.MacOS, Env(), Home, Tmp);
        Assert.Equal(mac.DataDir, mac.ConfigDir);

        var linux = AppPathsFactory.Create(HostOs.Linux, Env(), Home, Tmp);
        Assert.NotEqual(linux.DataDir, linux.ConfigDir);
    }

    [Theory]
    [InlineData(HostOs.MacOS)]
    [InlineData(HostOs.Linux)]
    public void Data_engine_logs_and_payloads_are_four_different_folders(HostOs os)
    {
        var p = AppPathsFactory.Create(os, Env(), Home, Tmp);

        var folders = new[] { p.DataDir, p.EngineDir, p.LogsDir, p.PayloadsDir };

        Assert.Equal(folders.Length, folders.Distinct().Count());
    }
}

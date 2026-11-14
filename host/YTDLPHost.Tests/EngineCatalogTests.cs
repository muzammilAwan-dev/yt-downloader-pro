using System.Runtime.InteropServices;
using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Phase 3, step 3.5: the engine files per OS and CPU. The names come from the projects' release pages (see EngineCatalog).</summary>
public class EngineCatalogTests
{
    public static IEnumerable<object[]> Supported() => new[]
    {
        new object[] { HostOs.Windows, Architecture.X64 }, new object[] { HostOs.Windows, Architecture.Arm64 },
        new object[] { HostOs.MacOS, Architecture.X64 }, new object[] { HostOs.MacOS, Architecture.Arm64 },
        new object[] { HostOs.Linux, Architecture.X64 }, new object[] { HostOs.Linux, Architecture.Arm64 }
    };

    private static string Last(string url) => url.Substring(url.LastIndexOf('/') + 1);

    [Theory]
    [MemberData(nameof(Supported))]
    public void Every_download_comes_from_a_github_release_and_has_a_checksum_source(HostOs os, Architecture arch)
    {
        var assets = EngineCatalog.For(os, arch)!;

        Assert.NotNull(assets);
        Assert.StartsWith("https://github.com/yt-dlp/yt-dlp/releases/latest/download/", assets.YtDlpUrl);
        Assert.Equal("https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS", assets.YtDlpSumsUrl);
        Assert.StartsWith("https://github.com/denoland/deno/releases/latest/download/", assets.DenoZipUrl);
        Assert.Equal(assets.DenoZipUrl + ".sha256sum", assets.DenoSumsUrl);
        if (!assets.FfmpegFromSystem)
        {
            Assert.StartsWith("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/", assets.FfmpegZipUrl);
            Assert.Equal("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/checksums.sha256", assets.FfmpegSumsUrl);
        }
    }

    [Fact]
    public void Windows_x64_is_the_set_the_app_has_always_used()
    {
        var assets = EngineCatalog.For(HostOs.Windows, Architecture.X64)!;

        Assert.Same(EngineAssets.Windows, assets);
        Assert.Equal(("yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe"), (assets.YtDlpFile, assets.FfmpegFile, assets.FfprobeFile, assets.DenoFile));
        Assert.Equal("yt-dlp.exe", Last(assets.YtDlpUrl));
        Assert.Equal("ffmpeg-master-latest-win64-gpl.zip", Last(assets.FfmpegZipUrl));
        Assert.Equal("deno-x86_64-pc-windows-msvc.zip", Last(assets.DenoZipUrl));
        Assert.False(assets.FfmpegFromSystem);
    }

    [Fact]
    public void Windows_arm64_gets_the_arm_builds_but_keeps_the_exe_names()
    {
        var assets = EngineCatalog.For(HostOs.Windows, Architecture.Arm64)!;

        Assert.Equal("yt-dlp.exe", assets.YtDlpFile);
        Assert.Equal("yt-dlp_arm64.exe", Last(assets.YtDlpUrl));
        Assert.Equal("ffmpeg-master-latest-winarm64-gpl.zip", Last(assets.FfmpegZipUrl));
        Assert.Equal("deno-aarch64-pc-windows-msvc.zip", Last(assets.DenoZipUrl));
    }

    [Theory]
    [InlineData(HostOs.MacOS, Architecture.X64, "yt-dlp_macos", "deno-x86_64-apple-darwin.zip")]
    [InlineData(HostOs.MacOS, Architecture.Arm64, "yt-dlp_macos", "deno-aarch64-apple-darwin.zip")]
    [InlineData(HostOs.Linux, Architecture.X64, "yt-dlp_linux", "deno-x86_64-unknown-linux-gnu.zip")]
    [InlineData(HostOs.Linux, Architecture.Arm64, "yt-dlp_linux_aarch64", "deno-aarch64-unknown-linux-gnu.zip")]
    public void Unix_sets_use_the_release_names_of_the_projects_and_plain_file_names_on_disk(HostOs os, Architecture arch, string ytDlpAsset, string denoAsset)
    {
        var assets = EngineCatalog.For(os, arch)!;

        Assert.Equal(ytDlpAsset, Last(assets.YtDlpUrl));
        Assert.Equal(denoAsset, Last(assets.DenoZipUrl));
        Assert.Equal(("yt-dlp", "ffmpeg", "ffprobe", "deno"), (assets.YtDlpFile, assets.FfmpegFile, assets.FfprobeFile, assets.DenoFile));
        Assert.True(assets.FfmpegFromSystem);
        Assert.Null(assets.FfmpegSumsUrl);
    }

    [Theory]
    [InlineData(Architecture.X86)]
    [InlineData(Architecture.Arm)]
    public void Other_cpus_are_not_supported(Architecture arch)
    {
        foreach (var os in new[] { HostOs.Windows, HostOs.MacOS, HostOs.Linux })
            Assert.Null(EngineCatalog.For(os, arch));
    }

    [Fact]
    public void The_current_platform_has_a_set_on_the_machines_that_run_the_tests()
    {
        Assert.NotNull(EngineCatalog.ForCurrentPlatform());
    }

    [Fact]
    public void A_set_without_checksum_urls_still_means_what_it_always_did()
    {
        var plain = new EngineAssets("a", "b", "c", "d", "https://x/a", "https://x/b", "https://x/d");

        Assert.Null(plain.YtDlpSumsUrl);
        Assert.False(plain.FfmpegFromSystem);
    }
}

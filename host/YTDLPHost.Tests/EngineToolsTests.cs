using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Step 3.5b: where the three engine tools are for a given asset set (downloaded into the engine folder, or ffmpeg from the system).</summary>
public class EngineToolsTests
{
    private sealed class Paths : IAppPaths
    {
        public string HomeDir => "/home/u";
        public string DownloadsDir => "/home/u/Downloads";
        public string EngineDir => Path.Combine(Path.GetTempPath(), "engine-tools-test");
        public string DataDir => "/data";
        public string LogsDir => "/logs";
        public string PayloadsDir => "/payloads";
        public string TempDir => "/tmp";
    }

    private static readonly EngineAssets Managed = new("yt-dlp", "ffmpeg", "ffprobe", "deno", "https://x/yt-dlp", "https://x/ffmpeg.zip", "https://x/deno.zip");
    private static readonly EngineAssets SystemFfmpeg = new("yt-dlp", "ffmpeg", "ffprobe", "deno", "https://x/yt-dlp", "", "https://x/deno.zip");

    private static ToolSearch Linux(string? path, params string[] existing) => new(HostOs.Linux, path, new HashSet<string>(existing).Contains);

    [Fact]
    public void Downloaded_tools_are_in_the_engine_folder_and_ffmpeg_is_passed_as_a_file()
    {
        var tools = EngineTools.Resolve(new Paths(), Managed, Linux("/usr/bin", "/usr/bin/ffmpeg", "/usr/bin/ffprobe"));

        string engine = new Paths().EngineDir;
        Assert.Equal(Path.Combine(engine, "yt-dlp"), tools.YtDlpPath);
        Assert.Equal(Path.Combine(engine, "deno"), tools.DenoPath);
        Assert.Equal(Path.Combine(engine, "ffmpeg"), tools.FfmpegPath);
        Assert.Equal(tools.FfmpegPath, tools.FfmpegLocation);    // unchanged from before: the system ffmpeg is ignored
        Assert.False(tools.FfmpegFromSystem);
    }

    [Fact]
    public void A_system_ffmpeg_is_passed_as_its_folder_so_ffprobe_is_found_beside_it()
    {
        var tools = EngineTools.Resolve(new Paths(), SystemFfmpeg, Linux("/usr/bin", "/usr/bin/ffmpeg", "/usr/bin/ffprobe"));

        Assert.True(tools.FfmpegFromSystem);
        Assert.True(tools.FfmpegFound);
        Assert.Equal("/usr/bin/ffmpeg", tools.FfmpegPath);
        Assert.Equal("/usr/bin", tools.FfmpegLocation);
        Assert.Equal(Path.Combine(new Paths().EngineDir, "yt-dlp"), tools.YtDlpPath);   // yt-dlp and deno are always ours
    }

    [Fact]
    public void A_system_ffmpeg_without_ffprobe_beside_it_counts_as_missing()
    {
        var onlyFfmpeg = EngineTools.Resolve(new Paths(), SystemFfmpeg, Linux("/usr/bin", "/usr/bin/ffmpeg"));
        Assert.False(onlyFfmpeg.FfmpegFound);
        Assert.Equal("", onlyFfmpeg.FfmpegLocation);

        // ffprobe in another folder does not help: yt-dlp is given one folder
        var split = EngineTools.Resolve(new Paths(), SystemFfmpeg, Linux("/opt/a:/usr/bin", "/opt/a/ffmpeg", "/usr/bin/ffprobe"));
        Assert.False(split.FfmpegFound);
    }

    [Fact]
    public void A_system_ffmpeg_that_is_nowhere_is_reported_as_not_found()
    {
        var tools = EngineTools.Resolve(new Paths(), SystemFfmpeg, Linux("/usr/bin"));

        Assert.True(tools.FfmpegFromSystem);
        Assert.False(tools.FfmpegFound);
        Assert.Equal("", tools.FfmpegPath);
    }
}

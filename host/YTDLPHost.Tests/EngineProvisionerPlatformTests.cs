using System.IO.Compression;
using System.Net;
using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Step 3.5b: the engine installer on macOS and Linux (system ffmpeg, executable bit, default asset set).</summary>
public class EngineProvisionerPlatformTests : IDisposable
{
    private const string YtDlpUrl = "https://test.invalid/yt-dlp";
    private const string FfmpegUrl = "https://test.invalid/ffmpeg.zip";
    private const string DenoUrl = "https://test.invalid/deno.zip";

    private static readonly EngineAssets SystemFfmpeg = new("yt-dlp", "ffmpeg", "ffprobe", "deno", YtDlpUrl, "", DenoUrl);
    private static readonly EngineAssets Downloaded = new("yt-dlp", "ffmpeg", "ffprobe", "deno", YtDlpUrl, FfmpegUrl, DenoUrl);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ytdlp-engine-platform-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    private sealed class TestPaths : IAppPaths
    {
        private readonly string _root;
        public TestPaths(string root) => _root = root;
        public string HomeDir => Path.Combine(_root, "home");
        public string DownloadsDir => Path.Combine(HomeDir, "Downloads");
        public string EngineDir => Path.Combine(_root, "engine");
        public string DataDir => Path.Combine(_root, "data");
        public string LogsDir => Path.Combine(_root, "logs");
        public string PayloadsDir => Path.Combine(_root, "payloads");
        public string TempDir => Path.Combine(_root, "tmp");
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Responses { get; } = new();
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            if (!Responses.TryGetValue(url, out var body)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    private static byte[] Zip(params (string name, string content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var e = zip.CreateEntry(name);
                using var w = new StreamWriter(e.Open());
                w.Write(content);
            }
        }
        return ms.ToArray();
    }

    private (EngineProvisioner engine, FakeHandler handler, TestPaths paths) Create(EngineAssets assets, params string[] systemFiles)
    {
        var paths = new TestPaths(_root);
        Directory.CreateDirectory(paths.TempDir);
        var handler = new FakeHandler();
        handler.Responses[YtDlpUrl] = "YTDLP"u8.ToArray();
        handler.Responses[FfmpegUrl] = Zip(("bin/ffmpeg", "FFMPEG"), ("bin/ffprobe", "FFPROBE"));
        handler.Responses[DenoUrl] = Zip(("deno", "DENO"));
        var search = new ToolSearch(HostOs.Linux, "/usr/bin", new HashSet<string>(systemFiles).Contains);
        return (new EngineProvisioner(paths, new HttpClient(handler), assets, TimeSpan.Zero, search), handler, paths);
    }

    [Fact]
    public async Task A_missing_system_ffmpeg_still_installs_the_others_and_is_then_reported()
    {
        var (engine, handler, paths) = Create(SystemFfmpeg);
        Assert.False(engine.AllPresent());

        var ex = await Assert.ThrowsAsync<EngineMissingToolException>(() => engine.InstallMissingAsync());

        Assert.Equal("ffmpeg", ex.Tool);
        Assert.Equal(EngineProvisioner.MissingFfmpegMessage, ex.Message);
        Assert.Equal("YTDLP", File.ReadAllText(Path.Combine(paths.EngineDir, "yt-dlp")));
        Assert.Equal("DENO", File.ReadAllText(Path.Combine(paths.EngineDir, "deno")));
        Assert.DoesNotContain(FfmpegUrl, handler.Requests);
        Assert.False(engine.AllPresent());
    }

    [Fact]
    public async Task A_found_system_ffmpeg_is_not_downloaded_and_counts_as_present()
    {
        var (engine, handler, paths) = Create(SystemFfmpeg, "/usr/bin/ffmpeg", "/usr/bin/ffprobe");

        await engine.InstallMissingAsync();

        Assert.True(engine.AllPresent());
        Assert.Equal("/usr/bin/ffmpeg", engine.FfmpegPath);
        Assert.DoesNotContain(FfmpegUrl, handler.Requests);
        Assert.False(File.Exists(Path.Combine(paths.EngineDir, "ffmpeg")));
    }

    [Fact]
    public async Task On_unix_every_installed_tool_is_executable()
    {
        if (OperatingSystem.IsWindows()) return;   // file modes exist only on Unix; the Linux CI job runs this
        var (engine, _, paths) = Create(Downloaded);

        await engine.InstallMissingAsync();

        const UnixFileMode Run = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        foreach (var name in new[] { "yt-dlp", "ffmpeg", "ffprobe", "deno" })
            Assert.Equal(Run, File.GetUnixFileMode(Path.Combine(paths.EngineDir, name)) & Run);
    }

    [Fact]
    public void Without_assets_the_set_of_this_computer_is_used()
    {
        var (_, handler, paths) = Create(Downloaded);
        var engine = new EngineProvisioner(paths, new HttpClient(handler));

        Assert.Equal(EngineCatalog.ForCurrentPlatformOrDefault().YtDlpFile, Path.GetFileName(engine.YtDlpPath));
        Assert.Equal(EngineCatalog.ForCurrentPlatformOrDefault().DenoFile, Path.GetFileName(engine.DenoPath));
    }
}

using System.IO.Compression;
using System.Net;
using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

public class EngineProvisionerTests : IDisposable
{
    private const string YtDlpUrl = "https://test.invalid/yt-dlp.exe";
    private const string FfmpegUrl = "https://test.invalid/ffmpeg.zip";
    private const string DenoUrl = "https://test.invalid/deno.zip";

    private static readonly EngineAssets Assets = new(
        "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe", YtDlpUrl, FfmpegUrl, DenoUrl);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ytdlp-engine-tests-" + Guid.NewGuid());

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
        public Dictionary<string, int> FailFirst { get; } = new();
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            if (FailFirst.TryGetValue(url, out var left) && left > 0)
            {
                FailFirst[url] = left - 1;
                throw new HttpRequestException("simulated network drop");
            }
            if (!Responses.TryGetValue(url, out var body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
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

    private (EngineProvisioner engine, FakeHandler handler, TestPaths paths) Create()
    {
        var paths = new TestPaths(_root);
        Directory.CreateDirectory(paths.TempDir);
        var handler = new FakeHandler();
        handler.Responses[YtDlpUrl] = "YTDLP"u8.ToArray();
        handler.Responses[FfmpegUrl] = Zip(
            ("ffmpeg-master/bin/ffmpeg.exe", "FFMPEG"),
            ("ffmpeg-master/bin/ffprobe.exe", "FFPROBE"),
            ("ffmpeg-master/bin/ffplay.exe", "FFPLAY"),
            ("ffmpeg-master/LICENSE.txt", "license"));
        handler.Responses[DenoUrl] = Zip(("deno.exe", "DENO"));
        var engine = new EngineProvisioner(paths, new HttpClient(handler), Assets, TimeSpan.Zero);
        return (engine, handler, paths);
    }

    [Fact]
    public void AllPresent_is_false_for_an_empty_folder_and_creates_it()
    {
        var (engine, _, paths) = Create();

        Assert.False(engine.AllPresent());
        Assert.True(Directory.Exists(paths.EngineDir));
    }

    [Fact]
    public async Task InstallMissing_downloads_extracts_and_places_all_three_tools()
    {
        var (engine, _, paths) = Create();
        var stages = new List<EngineStage>();

        await engine.InstallMissingAsync(stages.Add);

        Assert.Equal("YTDLP", File.ReadAllText(Path.Combine(paths.EngineDir, "yt-dlp.exe")));
        Assert.Equal("FFMPEG", File.ReadAllText(Path.Combine(paths.EngineDir, "ffmpeg.exe")));
        Assert.Equal("FFPROBE", File.ReadAllText(Path.Combine(paths.EngineDir, "ffprobe.exe")));
        Assert.Equal("DENO", File.ReadAllText(Path.Combine(paths.EngineDir, "deno.exe")));
        Assert.False(File.Exists(Path.Combine(paths.EngineDir, "ffplay.exe")));   // only ffmpeg + ffprobe are kept

        Assert.Empty(Directory.GetFileSystemEntries(paths.TempDir));              // archives and extract folders cleaned up
        Assert.True(engine.AllPresent());
        Assert.Equal(new[]
        {
            EngineStage.DownloadingYtDlp, EngineStage.DownloadingFfmpeg, EngineStage.ExtractingFfmpeg,
            EngineStage.DownloadingDeno, EngineStage.ExtractingDeno
        }, stages);
    }

    [Fact]
    public async Task InstallMissing_leaves_existing_tools_alone()
    {
        var (engine, handler, paths) = Create();
        Directory.CreateDirectory(paths.EngineDir);
        File.WriteAllText(Path.Combine(paths.EngineDir, "yt-dlp.exe"), "KEEP");
        File.WriteAllText(Path.Combine(paths.EngineDir, "ffmpeg.exe"), "KEEP");
        var stages = new List<EngineStage>();

        await engine.InstallMissingAsync(stages.Add);

        Assert.Equal("KEEP", File.ReadAllText(Path.Combine(paths.EngineDir, "yt-dlp.exe")));
        Assert.Equal(new[] { EngineStage.DownloadingDeno, EngineStage.ExtractingDeno }, stages);
        Assert.Equal(new[] { DenoUrl }, handler.Requests);
    }

    [Fact]
    public async Task A_dropped_connection_is_retried()
    {
        var (engine, handler, paths) = Create();
        handler.FailFirst[YtDlpUrl] = 1;

        await engine.InstallMissingAsync();

        Assert.Equal(2, handler.Requests.Count(r => r == YtDlpUrl));
        Assert.True(File.Exists(Path.Combine(paths.EngineDir, "yt-dlp.exe")));
    }

    [Fact]
    public async Task Persistent_network_failure_gives_up_after_three_attempts()
    {
        var (engine, handler, _) = Create();
        handler.FailFirst[YtDlpUrl] = 99;

        await Assert.ThrowsAsync<HttpRequestException>(() => engine.InstallMissingAsync());

        Assert.Equal(3, handler.Requests.Count(r => r == YtDlpUrl));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

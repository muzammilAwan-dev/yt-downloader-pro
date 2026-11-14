using System.IO.Compression;
using System.Net;
using System.Text;
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
        public Dictionary<string, Queue<byte[]>> Queued { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            if (Queued.TryGetValue(url, out var queue) && queue.Count > 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(queue.Dequeue()) });
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

    // ---------- checksum verification (step 3.5) ----------

    private const string YtDlpSumsUrl = "https://test.invalid/SHA2-256SUMS";
    private const string FfmpegSumsUrl = "https://test.invalid/checksums.sha256";
    private const string DenoSumsUrl = "https://test.invalid/deno.zip.sha256sum";

    private static readonly EngineAssets VerifiedAssets = new(
        "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe", YtDlpUrl, FfmpegUrl, DenoUrl, YtDlpSumsUrl, FfmpegSumsUrl, DenoSumsUrl);

    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Like Create(), but with checksum files that match the fake downloads: a GNU list with decoys, a list with CRLF and a binary marker, and a bare hash.</summary>
    private (EngineProvisioner engine, FakeHandler handler, TestPaths paths) CreateVerified()
    {
        var (_, handler, paths) = Create();
        handler.Responses[YtDlpSumsUrl] = Text($"{new string('a', 64)}  yt-dlp_macos\n{Sha256Hex.Of(handler.Responses[YtDlpUrl])}  yt-dlp.exe\n");
        handler.Responses[FfmpegSumsUrl] = Text($"{Sha256Hex.Of(handler.Responses[FfmpegUrl])} *ffmpeg.zip\r\n");
        handler.Responses[DenoSumsUrl] = Text(Sha256Hex.Of(handler.Responses[DenoUrl]) + "\n");
        return (new EngineProvisioner(paths, new HttpClient(handler), VerifiedAssets, TimeSpan.Zero), handler, paths);
    }

    [Fact]
    public async Task Downloads_are_installed_when_the_published_checksums_match()
    {
        var (engine, handler, paths) = CreateVerified();

        await engine.InstallMissingAsync();

        Assert.True(engine.AllPresent());
        Assert.Equal("YTDLP", File.ReadAllText(engine.YtDlpPath));
        Assert.Contains(YtDlpSumsUrl, handler.Requests);
        Assert.Contains(FfmpegSumsUrl, handler.Requests);
        Assert.Contains(DenoSumsUrl, handler.Requests);
        Assert.Empty(Directory.GetFiles(paths.EngineDir, "*.download"));
    }

    [Fact]
    public async Task A_checksum_mismatch_installs_nothing_and_is_tried_twice()
    {
        var (engine, handler, paths) = CreateVerified();
        handler.Responses[YtDlpSumsUrl] = Text(new string('0', 64) + "  yt-dlp.exe\n");

        var ex = await Assert.ThrowsAsync<EngineVerificationException>(() => engine.InstallMissingAsync());

        Assert.Contains("yt-dlp.exe", ex.Message);
        Assert.False(File.Exists(engine.YtDlpPath));
        Assert.Empty(Directory.GetFiles(paths.EngineDir, "*.download"));
        Assert.Equal(2, handler.Requests.Count(url => url == YtDlpUrl));
    }

    [Fact]
    public async Task A_release_that_changed_between_the_two_downloads_passes_on_the_second_try()
    {
        var (engine, handler, _) = CreateVerified();
        handler.Queued[YtDlpSumsUrl] = new Queue<byte[]>(new[] { Text(new string('0', 64) + "  yt-dlp.exe\n") });   // the first answer is stale, the next is the normal one

        await engine.InstallMissingAsync();

        Assert.Equal("YTDLP", File.ReadAllText(engine.YtDlpPath));
        Assert.Equal(2, handler.Requests.Count(url => url == YtDlpUrl));
    }

    [Fact]
    public async Task A_checksum_file_without_an_entry_for_the_asset_is_refused_at_once()
    {
        var (engine, handler, _) = CreateVerified();
        handler.Responses[YtDlpSumsUrl] = Text(new string('a', 64) + "  something-else.exe\n");

        await Assert.ThrowsAsync<EngineVerificationException>(() => engine.InstallMissingAsync());

        Assert.False(File.Exists(engine.YtDlpPath));
        Assert.Equal(1, handler.Requests.Count(url => url == YtDlpUrl));
    }

    [Fact]
    public async Task Unavailable_checksums_fail_the_install_instead_of_skipping_the_check()
    {
        var (engine, handler, _) = CreateVerified();
        handler.Responses.Remove(YtDlpSumsUrl);

        await Assert.ThrowsAsync<HttpRequestException>(() => engine.InstallMissingAsync());

        Assert.False(File.Exists(engine.YtDlpPath));
    }

    [Fact]
    public async Task A_tampered_ffmpeg_archive_installs_no_ffmpeg_files()
    {
        var (engine, handler, paths) = CreateVerified();
        handler.Responses[FfmpegSumsUrl] = Text(new string('0', 64) + "  ffmpeg.zip\n");

        await Assert.ThrowsAsync<EngineVerificationException>(() => engine.InstallMissingAsync());

        Assert.False(File.Exists(Path.Combine(paths.EngineDir, "ffmpeg.exe")));
        Assert.False(File.Exists(Path.Combine(paths.EngineDir, "ffprobe.exe")));
    }

    [Fact]
    public async Task A_leftover_partial_download_does_not_block_the_install()
    {
        var (engine, _, paths) = CreateVerified();
        Directory.CreateDirectory(paths.EngineDir);
        File.WriteAllText(Path.Combine(paths.EngineDir, "yt-dlp.exe.download"), "half a fi");

        await engine.InstallMissingAsync();

        Assert.Equal("YTDLP", File.ReadAllText(engine.YtDlpPath));
        Assert.Empty(Directory.GetFiles(paths.EngineDir, "*.download"));
    }
}

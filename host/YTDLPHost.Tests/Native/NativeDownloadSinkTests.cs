using System.Collections.ObjectModel;
using System.Text;
using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Native;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Tests.Native;

/// <summary>Phase 2, step 2.3: browser requests enter the real intake and list. Cookie files go to a private temp folder per test.</summary>
public class NativeDownloadSinkTests : IDisposable
{
    private const string Vimeo = "https://vimeo.com/123456789";
    private const string YouTube = "https://www.youtube.com/watch?v=abcdefghijk";

    // Own folder per test: the global AppPaths.Current is swapped by AppPathsTests while other test classes run, so using it here raced with that test.
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

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ytdlp-sink-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ObservableCollection<DownloadItemViewModel> _list = new();
    private readonly List<string> _added = new();
    private int _afterAdd;
    private string? _unavailable;

    private NativeDownloadSink Sink()
    {
        var ui = new InlineUiDispatcher();
        var intake = new DownloadIntake(new CommandIngest(new TestPaths(_root)), _list, ui);
        return new NativeDownloadSink(intake, _list, ui, vm => _added.Add(vm.Task.Command), () => _afterAdd++, () => _unavailable);
    }

    private static IReadOnlyList<MappedDownload> Map(string optionsJson, string? authJson, params string[] urls)
    {
        var list = string.Join(",", urls.Select(u => $"\"{u}\""));
        var auth = authJson == null ? "" : $",\"auth\":{authJson}";
        var json = $"{{\"v\":1,\"id\":\"s1\",\"type\":\"download\",\"urls\":[{list}],\"options\":{{{optionsJson}}}{auth}}}";
        var parsed = NativeRequestParser.Parse(Encoding.UTF8.GetBytes(json));
        Assert.True(parsed.Ok, parsed.Error?.Message);
        var result = DownloadRequestMapper.Map((DownloadRequest)parsed.Request!, new MapperSettings(""));
        Assert.True(result.Ok, result.Error);
        return result.Downloads;
    }

    private static IReadOnlyList<MappedDownload> MapOne(string url = Vimeo, string? authJson = null) => Map("\"quality\":\"1080\"", authJson, url);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Every_download_becomes_a_queued_item_and_the_queue_is_started_once()
    {
        var result = Sink().Submit(Map("\"quality\":\"720\"", null, "https://vimeo.com/1", "https://vimeo.com/2"));

        Assert.Null(result.Error);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, _list.Count);
        Assert.All(_list, item => Assert.Equal(DownloadStatus.Queued, item.Task.Status));
        Assert.Equal(2, _added.Count);
        Assert.Equal(1, _afterAdd);
    }

    [Fact]
    public void The_item_carries_the_mapped_command_and_no_link_payload()
    {
        var mapped = MapOne();

        var item = Assert.Single(Sink().Submit(mapped).Items);

        Assert.Equal(mapped[0].Command, item.Task.Command);
        Assert.Equal("", item.Task.UrlPayload);
    }

    [Fact]
    public void Cookies_go_to_a_temp_file_and_never_into_the_command()
    {
        var item = Assert.Single(Sink().Submit(MapOne(authJson: "{\"cookies\":\"# session-cookie-text\"}")).Items);

        Assert.True(File.Exists(item.Task.CookieFilePath));
        Assert.Equal("# session-cookie-text", File.ReadAllText(item.Task.CookieFilePath));
        Assert.Equal("# session-cookie-text", item.Task.CookiePayload);
        Assert.DoesNotContain("session-cookie-text", item.Task.Command);
    }

    [Theory]
    [InlineData(Vimeo, "Mozilla/5.0 Test", true)]
    [InlineData(YouTube, "Mozilla/5.0 Test", false)]   // not for YouTube
    [InlineData(Vimeo, "Mozilla\\\" --exec calc", false)] // a quote would end the argument: the User-Agent is skipped
    public void The_user_agent_is_added_only_when_it_is_safe_and_the_site_is_not_YouTube(string url, string userAgent, bool expected)
    {
        var item = Assert.Single(Sink().Submit(MapOne(url, $"{{\"userAgent\":\"{userAgent}\"}}")).Items);

        Assert.Equal(expected, item.Task.Command.Contains("--user-agent"));
        Assert.DoesNotContain("--exec", item.Task.Command);
    }

    [Fact]
    public void The_same_download_twice_follows_the_existing_item_and_adds_nothing()
    {
        var sink = Sink();
        var first = Assert.Single(sink.Submit(MapOne(authJson: "{\"cookies\":\"# c\"}")).Items);

        var second = Assert.Single(sink.Submit(MapOne(authJson: "{\"cookies\":\"# c\"}")).Items);

        Assert.Same(first, second);
        Assert.Single(_list);
    }

    [Fact]
    public void An_app_that_cannot_take_downloads_adds_nothing()
    {
        _unavailable = "setup failed";

        var result = Sink().Submit(MapOne());

        Assert.Empty(result.Items);
        Assert.Equal("setup failed", result.Error);
        Assert.Empty(_list);
        Assert.Equal(0, _afterAdd);
    }

    [Fact]
    public void A_command_the_checker_refuses_adds_nothing()
    {
        var bad = new MappedDownload(new[] { "--exec", "calc" }, "yt-dlp --exec calc \"https://vimeo.com/1\"", null, null);

        var result = Sink().Submit(new[] { bad });

        Assert.Empty(result.Items);
        Assert.Contains("command checker", result.Error);
        Assert.Empty(_list);
    }

    [Fact]
    public void Listed_ids_follow_the_list()
    {
        var sink = Sink();
        var item = Assert.Single(sink.Submit(MapOne()).Items);
        Assert.Contains(item.Id, sink.ListedIds());

        _list.Remove(item);

        Assert.DoesNotContain(item.Id, sink.ListedIds());
    }
}

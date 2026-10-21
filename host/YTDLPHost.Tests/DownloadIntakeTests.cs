using System.Collections.ObjectModel;
using System.Text;
using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Tests;

public class DownloadIntakeTests : IDisposable
{
    private const string Cmd = "yt-dlp \"https://www.youtube.com/watch?v=dQw4w9WgXcQ\"";
    private const string OtherCmd = "yt-dlp \"https://vimeo.com/123456789\"";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ytdlp-intake-tests-" + Guid.NewGuid());

    private sealed class TempPaths : IAppPaths
    {
        private readonly string _root;
        public TempPaths(string root) => _root = root;
        public string HomeDir => Path.Combine(_root, "home");
        public string DownloadsDir => Path.Combine(HomeDir, "Downloads");
        public string EngineDir => Path.Combine(_root, "engine");
        public string DataDir => Path.Combine(_root, "data");
        public string LogsDir => Path.Combine(_root, "logs");
        public string PayloadsDir => Path.Combine(_root, "payloads");
        public string TempDir => Path.Combine(_root, "tmp");
    }

    private readonly ObservableCollection<DownloadItemViewModel> _list = new();

    private DownloadIntake Intake(Action<DownloadItemViewModel>? attach = null) =>
        new(new CommandIngest(new TempPaths(_root)), _list, new InlineUiDispatcher(), null, attach);

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    private static string Link(string command, string? cookies = null)
    {
        var s = "ytdlp://" + B64(command);
        if (cookies != null) s += "||" + B64(cookies);
        return s;
    }

    private int CookieFileCount()
    {
        var dir = new TempPaths(_root).TempDir;
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "ytdlp_cookies_*.txt").Length : 0;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public void A_valid_link_adds_one_queued_item_built_from_the_command()
    {
        var result = Intake().Submit(Link(Cmd));

        Assert.Equal(IntakeStatus.Added, result.Status);
        var item = Assert.Single(_list);
        Assert.Same(item, result.Item);
        Assert.Equal(DownloadStatus.Queued, item.Task.Status);
        Assert.Equal(Cmd, item.Task.Command);
        Assert.Equal(CommandInfo.ExtractResolution(Cmd), item.Task.Resolution);
        Assert.Equal(CommandInfo.ExtractTitleHint(Cmd), item.Task.Title);
        Assert.Equal(Link(Cmd), item.Task.UrlPayload);
        Assert.Equal("", item.Task.CookiePayload);
    }

    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("ytdlp://")]
    [InlineData("   ")]
    public void Links_that_are_not_a_payload_are_ignored(string link)
    {
        var result = Intake().Submit(link);

        Assert.Equal(IntakeStatus.Ignored, result.Status);
        Assert.Empty(_list);
    }

    [Fact]
    public void A_null_link_is_ignored()
    {
        Assert.Equal(IntakeStatus.Ignored, Intake().Submit(null).Status);
        Assert.Empty(_list);
    }

    [Fact]
    public void A_command_the_validator_rejects_is_blocked_with_its_reason_and_nothing_is_added()
    {
        var result = Intake().Submit(Link("yt-dlp --exec calc \"https://a.com\"", cookies: "# cookies"));

        Assert.Equal(IntakeStatus.Blocked, result.Status);
        Assert.Contains("--exec", result.BlockReason);
        Assert.Null(result.Item);
        Assert.Empty(_list);
        Assert.Equal(0, CookieFileCount());
    }

    [Fact]
    public void The_same_command_while_queued_is_a_duplicate()
    {
        var intake = Intake();
        intake.Submit(Link(Cmd));

        var second = intake.Submit(Link(Cmd));

        Assert.Equal(IntakeStatus.Duplicate, second.Status);
        Assert.Single(_list);
    }

    [Fact]
    public void The_same_command_while_downloading_is_a_duplicate()
    {
        var intake = Intake();
        intake.Submit(Link(Cmd));
        _list[0].Task.Status = DownloadStatus.Downloading;

        Assert.Equal(IntakeStatus.Duplicate, intake.Submit(Link(Cmd)).Status);
        Assert.Single(_list);
    }

    [Fact]
    public void The_same_command_after_the_first_finished_is_added_again()
    {
        var intake = Intake();
        intake.Submit(Link(Cmd));
        _list[0].Task.Status = DownloadStatus.Completed;

        var again = intake.Submit(Link(Cmd));

        Assert.Equal(IntakeStatus.Added, again.Status);
        Assert.Equal(2, _list.Count);
    }

    [Fact]
    public void A_different_command_is_not_a_duplicate()
    {
        var intake = Intake();
        intake.Submit(Link(Cmd));

        Assert.Equal(IntakeStatus.Added, intake.Submit(Link(OtherCmd)).Status);
        Assert.Equal(2, _list.Count);
    }

    [Fact]
    public void Cookies_in_the_link_are_written_to_a_file_and_kept_on_the_task()
    {
        const string cookies = "# Netscape HTTP Cookie File\n.vimeo.com\tTRUE\t/\tTRUE\t2000000000\tsession\tabc";

        var result = Intake().Submit(Link(OtherCmd, cookies));

        var task = result.Item!.Task;
        Assert.Equal(cookies, task.CookiePayload);
        Assert.True(File.Exists(task.CookieFilePath));
        Assert.Equal(cookies, File.ReadAllText(task.CookieFilePath));
    }

    [Fact]
    public void A_duplicate_link_with_cookies_leaves_no_second_cookie_file()
    {
        var intake = Intake();
        intake.Submit(Link(OtherCmd, "# cookies"));
        Assert.Equal(1, CookieFileCount());

        var second = intake.Submit(Link(OtherCmd, "# cookies"));

        Assert.Equal(IntakeStatus.Duplicate, second.Status);
        Assert.Equal(1, CookieFileCount());
    }

    [Fact]
    public void Attach_runs_before_the_item_is_in_the_list_and_onAdded_after()
    {
        bool? inListDuringAttach = null;
        bool? inListDuringOnAdded = null;
        DownloadItemViewModel? attached = null;

        var intake = Intake(vm => { attached = vm; inListDuringAttach = _list.Contains(vm); });
        var result = intake.Submit(Link(Cmd), vm => inListDuringOnAdded = _list.Contains(vm));

        Assert.Same(result.Item, attached);
        Assert.False(inListDuringAttach);
        Assert.True(inListDuringOnAdded);
    }

    [Fact]
    public void Attach_and_onAdded_are_not_called_for_ignored_blocked_or_duplicate_links()
    {
        int calls = 0;
        var intake = Intake(_ => calls++);
        intake.Submit(Link(Cmd), _ => calls++);
        Assert.Equal(2, calls);

        intake.Submit(Link(Cmd), _ => calls++);                                  // duplicate
        intake.Submit("https://example.com", _ => calls++);                      // ignored
        intake.Submit(Link("yt-dlp --exec calc \"https://a.com\""), _ => calls++); // blocked

        Assert.Equal(2, calls);
    }

    [Fact]
    public void Malformed_base64_still_throws_so_the_caller_can_log_and_drop_it()
    {
        Assert.ThrowsAny<FormatException>(() => Intake().Submit("ytdlp://!!!!"));
        Assert.Empty(_list);
    }

    [Fact]
    public void Identical_links_arriving_together_add_exactly_one_item()
    {
        var intake = Intake();
        var link = Link(Cmd);
        var results = new IntakeStatus[16];

        Parallel.For(0, results.Length, i => results[i] = intake.Submit(link).Status);

        Assert.Equal(1, results.Count(s => s == IntakeStatus.Added));
        Assert.Equal(results.Length - 1, results.Count(s => s == IntakeStatus.Duplicate));
        Assert.Single(_list);
    }
}

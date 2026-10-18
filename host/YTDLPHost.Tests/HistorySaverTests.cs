using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>The debounced history writer from step 1.5e, including the new flush-on-dispose.</summary>
public class HistorySaverTests
{
    private static DownloadTask Make(string title) => new() { Title = title, Command = "yt-dlp x" };

    private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 5000)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - started).TotalMilliseconds > timeoutMs) throw new TimeoutException("Timed out waiting for: " + what);
            await Task.Delay(5);
        }
    }

    private sealed class Saves
    {
        private readonly List<IReadOnlyList<DownloadTask>> _list = new();
        public void Add(IReadOnlyList<DownloadTask> snapshot) { lock (_list) _list.Add(snapshot); }
        public int Count { get { lock (_list) return _list.Count; } }
        public IReadOnlyList<DownloadTask> this[int i] { get { lock (_list) return _list[i]; } }
    }

    [Fact]
    public async Task Requests_close_together_are_saved_once_with_the_latest_snapshot()
    {
        int n = 0;
        var saves = new Saves();
        using var saver = new HistorySaver(new InlineUiDispatcher(), () => new[] { Make((++n).ToString()) }, saves.Add, TimeSpan.FromMilliseconds(40));

        saver.RequestSave();
        saver.RequestSave();
        saver.RequestSave();

        await WaitFor(() => saves.Count >= 1, "first save");
        await Task.Delay(200);

        Assert.Equal(1, saves.Count);
        Assert.Equal("3", saves[0][0].Title);
    }

    [Fact]
    public void Dispose_writes_a_save_that_is_still_waiting()
    {
        var saves = new Saves();
        var saver = new HistorySaver(new InlineUiDispatcher(), () => new[] { Make("last") }, saves.Add, TimeSpan.FromSeconds(30));

        saver.RequestSave();
        Assert.Equal(0, saves.Count);

        saver.Dispose();

        Assert.Equal(1, saves.Count);
        Assert.Equal("last", saves[0][0].Title);
    }

    [Fact]
    public void Dispose_with_nothing_waiting_writes_nothing()
    {
        var saves = new Saves();
        var saver = new HistorySaver(new InlineUiDispatcher(), () => new[] { Make("x") }, saves.Add, TimeSpan.FromMilliseconds(10));

        saver.Dispose();
        saver.Dispose();

        Assert.Equal(0, saves.Count);
    }

    [Fact]
    public async Task Requests_after_Dispose_are_ignored()
    {
        var saves = new Saves();
        var saver = new HistorySaver(new InlineUiDispatcher(), () => new[] { Make("x") }, saves.Add, TimeSpan.FromMilliseconds(10));
        saver.Dispose();

        saver.RequestSave();
        await Task.Delay(150);

        Assert.Equal(0, saves.Count);
    }

    [Fact]
    public async Task A_failing_save_never_throws_to_the_caller()
    {
        var saver = new HistorySaver(
            new InlineUiDispatcher(),
            () => new[] { Make("x") },
            _ => throw new IOException("disk full"),
            TimeSpan.FromMilliseconds(20));

        var ex = Record.Exception(() => saver.RequestSave());
        await Task.Delay(120); // the background write fails and is only logged
        var exOnDispose = Record.Exception(() => saver.Dispose());

        Assert.Null(ex);
        Assert.Null(exOnDispose);
    }
}

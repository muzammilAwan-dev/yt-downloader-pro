using System.Collections.ObjectModel;
using System.Diagnostics;
using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Tests;

/// <summary>
/// Tests for the scheduling rules extracted in Phase 1.5c: concurrency limit, same-video mutex,
/// slot accounting and runner failures. They use a fake runner and a dispatcher that runs work
/// inline under a lock, so no yt-dlp and no UI are involved. Start spacing is zero.
/// "Nothing more happens" assertions wait a short settle time (150 ms); everything else waits
/// for a condition with a timeout, so a failure is a clear message, not a hang.
/// </summary>
public class DownloadQueueTests
{
    private static string Cmd(int n) =>
        $"yt-dlp -f b -o \"out/%(title)s.mp4\" \"https://www.youtube.com/watch?v=video{n:D6}\"";

    private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Timed out waiting for: " + what);
            await Task.Delay(5);
        }
    }

    private static Task Settle() => Task.Delay(150);

    // ---------------------------------------------------------------- tests

    [Fact]
    public async Task Starts_up_to_the_limit_and_no_more()
    {
        using var h = new QueueHarness(max: 2);
        var a = h.Add(Cmd(1));
        var b = h.Add(Cmd(2));
        var c = h.Add(Cmd(3));

        h.Queue.Trigger();
        await WaitFor(() => h.RunnerCount == 2, "two runners");
        await Settle();

        Assert.Equal(2, h.RunnerCount);
        Assert.Equal(2, h.Queue.ActiveCount);
        Assert.Equal(DownloadStatus.Downloading, a.Task.Status);
        Assert.Equal(DownloadStatus.Downloading, b.Task.Status);
        Assert.Equal(DownloadStatus.Queued, c.Task.Status);
    }

    [Fact]
    public async Task Finishing_a_download_starts_the_next_queued_one()
    {
        using var h = new QueueHarness(max: 1);
        var a = h.Add(Cmd(1));
        var b = h.Add(Cmd(2));

        h.Queue.Trigger();
        await WaitFor(() => h.RunnerCount == 1, "first runner");

        h.Runner(0).Finish();

        await WaitFor(() => h.RunnerCount == 2, "second runner");
        await WaitFor(() => b.Task.Status == DownloadStatus.Downloading, "second item downloading");
        Assert.Equal(DownloadStatus.Completed, a.Task.Status);
    }

    [Fact]
    public async Task Same_video_waits_until_the_running_download_ends()
    {
        using var h = new QueueHarness(max: 2); // the limit is not what blocks the duplicate
        var a = h.Add(Cmd(7));
        var duplicate = h.Add(Cmd(7));
        var other = h.Add(Cmd(8));

        h.Queue.Trigger();
        await WaitFor(() => h.RunnerCount == 2, "two runners (first and the unrelated one)");
        await Settle();

        Assert.Equal(DownloadStatus.Downloading, a.Task.Status);
        Assert.Equal(DownloadStatus.Downloading, other.Task.Status);
        Assert.Equal(DownloadStatus.Queued, duplicate.Task.Status);
        Assert.Equal(DownloadQueue.PhaseWaitingSimilar, duplicate.Task.CurrentPhase);

        h.Runner(0).Finish(); // the first download of that video ends

        await WaitFor(() => duplicate.Task.Status == DownloadStatus.Downloading, "duplicate starts after the first ends");
        Assert.Equal(DownloadQueue.PhaseStarting, duplicate.Task.CurrentPhase);
    }

    [Fact]
    public async Task Nothing_starts_until_the_engine_is_ready()
    {
        using var h = new QueueHarness(max: 2, ready: false);
        var a = h.Add(Cmd(1));

        h.Queue.Trigger();
        await Settle();
        Assert.Equal(0, h.RunnerCount);
        Assert.Equal(DownloadStatus.Queued, a.Task.Status);

        h.Queue.MarkEngineReady();
        h.Queue.Trigger();

        await WaitFor(() => h.RunnerCount == 1, "runner after the engine is ready");
    }

    [Fact]
    public async Task TryStop_cancels_the_runner_and_frees_the_slot()
    {
        using var h = new QueueHarness(max: 1);
        var a = h.Add(Cmd(1));
        var b = h.Add(Cmd(2));

        h.Queue.Trigger();
        await WaitFor(() => h.RunnerCount == 1, "first runner");

        Assert.False(h.Queue.TryStop(Guid.NewGuid()));
        Assert.True(h.Queue.TryStop(a.Id));
        a.Task.Status = DownloadStatus.Paused; // what the caller does after stopping

        Assert.True(h.Runner(0).WasCancelled);
        await WaitFor(() => h.RunnerCount == 2, "next item takes the freed slot");
        await WaitFor(() => b.Task.Status == DownloadStatus.Downloading, "second item downloading");
    }

    [Fact]
    public async Task A_runner_that_throws_marks_the_item_failed_and_frees_the_slot()
    {
        using var h = new QueueHarness(max: 1)
        {
            OnCreate = (runner, index) =>
            {
                if (index == 0) runner.ThrowOnExecute = new InvalidOperationException("boom");
            }
        };
        var a = h.Add(Cmd(1));
        var b = h.Add(Cmd(2));

        h.Queue.Trigger();

        await WaitFor(() => a.Task.Status == DownloadStatus.Error, "first item marked as failed");
        Assert.Contains("boom", a.Task.ErrorMessage);
        await WaitFor(() => h.RunnerCount == 2, "second runner after the failure");
        await WaitFor(() => b.Task.Status == DownloadStatus.Downloading, "second item downloading");
        await WaitFor(() => h.Runner(0).WasDisposed, "failed runner disposed");
    }

    [Fact]
    public async Task A_stale_runner_cannot_unregister_the_runner_that_replaced_it()
    {
        // Pause then resume quickly: the old runner is still winding down when the new one starts.
        using var h = new QueueHarness(max: 1)
        {
            OnCreate = (runner, index) =>
            {
                if (index == 0) runner.CompleteOnCancel = false;
            }
        };
        var a = h.Add(Cmd(1));

        h.Queue.Trigger();
        await WaitFor(() => h.RunnerCount == 1, "first runner");

        Assert.True(h.Queue.TryStop(a.Id)); // slot freed, but runner 0 has not returned yet
        h.Ui.Invoke(() =>
        {
            a.Task.Status = DownloadStatus.Queued; // the user resumes
            a.Task.CurrentPhase = DownloadQueue.PhaseQueued;
        });
        h.Queue.Trigger();

        await WaitFor(() => h.RunnerCount == 2, "replacement runner");
        Assert.Equal(1, h.Queue.ActiveCount);

        h.Runner(0).FinishQuietly(); // the stale runner finally returns
        await WaitFor(() => h.Runner(0).WasDisposed, "stale runner disposed");

        Assert.Equal(1, h.Queue.ActiveCount);       // the replacement still holds its slot
        Assert.False(h.Runner(1).WasDisposed);

        var b = h.Add(Cmd(2));                      // and the limit still holds
        h.Queue.Trigger();
        await Settle();
        Assert.Equal(2, h.RunnerCount);
        Assert.Equal(DownloadStatus.Queued, b.Task.Status);
    }

    [Fact]
    public async Task SuspendToQueue_cancels_requeues_and_the_item_restarts()
    {
        using var h = new QueueHarness(max: 1);
        var a = h.Add(Cmd(1));

        h.Queue.Trigger();
        await WaitFor(() => h.RunnerCount == 1, "first runner");

        await h.Queue.SuspendToQueueAsync(a);

        Assert.True(h.Runner(0).WasCancelled);
        h.Queue.Trigger();

        await WaitFor(() => h.RunnerCount == 2, "restarted runner");
        await WaitFor(() => a.Task.Status == DownloadStatus.Downloading, "item downloading again");
    }

    [Fact]
    public async Task Raises_started_completed_and_drained_events()
    {
        using var h = new QueueHarness(max: 1);
        var a = h.Add(Cmd(1));

        h.Queue.Trigger();
        await WaitFor(() => h.HasEvent("started:" + a.Id), "started event");
        await WaitFor(() => h.RunnerCount == 1, "runner");

        h.Runner(0).Finish();

        await WaitFor(() => h.HasEvent("completed:t"), "completed event with the runner's title");
        await WaitFor(() => h.HasEvent("drained"), "drained event once the queue is idle");
    }

    [Fact]
    public async Task Many_concurrent_triggers_do_not_start_duplicate_runners()
    {
        using var h = new QueueHarness(max: 3);
        h.Add(Cmd(1));
        h.Add(Cmd(2));
        h.Add(Cmd(3));

        Parallel.For(0, 200, _ => h.Queue.Trigger());

        await WaitFor(() => h.RunnerCount == 3, "three runners");
        await Settle();
        Assert.Equal(3, h.RunnerCount);
        Assert.Equal(3, h.Queue.ActiveCount);
    }

    [Fact]
    public async Task Dispose_disposes_the_running_runners()
    {
        var h = new QueueHarness(max: 1);
        h.Add(Cmd(1));

        h.Queue.Trigger();
        await WaitFor(() => h.RunnerCount == 1, "runner");

        h.Queue.Dispose();

        Assert.True(h.Runner(0).WasDisposed);
        Assert.True(h.Queue.IsDisposed);
        h.Dispose(); // idempotent
    }

    // ---------------------------------------------------------------- test doubles

    /// <summary>Runs everything inline under one lock: deterministic, and safe for the shared item list.</summary>
    private sealed class TestUi : IUiDispatcher
    {
        private readonly object _gate = new();
        public void Invoke(Action action) { lock (_gate) action(); }
        public void Post(Action action) { lock (_gate) action(); }
        public void Post(UiPriority priority, Action action) { lock (_gate) action(); }
    }

    private sealed class FakeRunner : IDownloadRunner
    {
        private readonly TaskCompletionSource _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<ProgressEventArgs>? OnProgressUpdate;
        public event EventHandler<CompleteEventArgs>? OnDownloadComplete;
        public event EventHandler<DownloadErrorEventArgs>? OnDownloadError;
        public event EventHandler<ExtractedInfoEventArgs>? OnInfoExtracted;

        public bool CompleteOnCancel { get; set; } = true;
        public Exception? ThrowOnExecute { get; set; }
        private volatile DownloadTask? _item;
        public DownloadTask? Item => _item;
        public volatile bool WasCancelled;
        public volatile bool WasDisposed;

        public async Task ExecuteAsync(DownloadTask task)
        {
            _item = task;
            if (ThrowOnExecute != null) throw ThrowOnExecute;
            await _finish.Task;
        }

        public void Cancel()
        {
            WasCancelled = true;
            if (CompleteOnCancel) _finish.TrySetResult();
        }

        public void Dispose() => WasDisposed = true;

        /// <summary>Ends the download like the real runner does: status first, then the event, then ExecuteAsync returns.</summary>
        public void Finish(bool success = true)
        {
            // The queue calls ExecuteAsync right after creating the runner, on another thread; wait until it has.
            SpinWait.SpinUntil(() => _item != null, 5000);
            var task = _item ?? throw new InvalidOperationException("Runner was never started");
            if (success)
            {
                task.Status = DownloadStatus.Completed;
                OnDownloadComplete?.Invoke(this, new CompleteEventArgs(task.Id, "out.mp4", task.Title));
            }
            else
            {
                task.Status = DownloadStatus.Error;
                OnDownloadError?.Invoke(this, new DownloadErrorEventArgs(task.Id, "failed"));
            }
            _finish.TrySetResult();
        }

        /// <summary>Returns from ExecuteAsync without touching the task (a runner that was cancelled earlier).</summary>
        public void FinishQuietly() => _finish.TrySetResult();
    }

    private sealed class QueueHarness : IDisposable
    {
        private readonly List<FakeRunner> _runners = new();
        private readonly List<string> _events = new();

        public TestUi Ui { get; } = new();
        public ObservableCollection<DownloadItemViewModel> Items { get; } = new();
        public DownloadQueue Queue { get; }
        public int Max { get; set; }
        public Action<FakeRunner, int>? OnCreate { get; set; }

        public QueueHarness(int max, bool ready = true)
        {
            Max = max;
            Queue = new DownloadQueue(Items, Ui, CreateRunner, () => Max, TimeSpan.Zero);
            Queue.ItemStarted += vm => Record("started:" + vm.Id);
            Queue.ItemCompleted += (_, title) => Record("completed:" + title);
            Queue.Drained += () => Record("drained");
            if (ready) Queue.MarkEngineReady();
        }

        private FakeRunner CreateRunner()
        {
            lock (_runners)
            {
                var runner = new FakeRunner();
                OnCreate?.Invoke(runner, _runners.Count);
                _runners.Add(runner);
                return runner;
            }
        }

        private void Record(string name) { lock (_events) _events.Add(name); }
        public bool HasEvent(string name) { lock (_events) return _events.Contains(name); }

        public int RunnerCount { get { lock (_runners) return _runners.Count; } }
        public FakeRunner Runner(int index) { lock (_runners) return _runners[index]; }

        public DownloadItemViewModel Add(string command)
        {
            var vm = new DownloadItemViewModel(new DownloadTask { Command = command, Title = "t" });
            Ui.Invoke(() => Items.Add(vm));
            return vm;
        }

        public void Dispose() => Queue.Dispose();
    }
}

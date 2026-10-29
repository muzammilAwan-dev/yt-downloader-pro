using System.Diagnostics;
using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>
/// When the engine update runs: at launch, again after the interval, only while the queue is idle, and never able to leave the queue held.
/// Uses a fake gate and a fake update, with intervals of a few milliseconds; conditions are waited for with a timeout.
/// </summary>
public class EngineUpdateSchedulerTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private sealed class FakeGate : IEngineUpdateGate
    {
        private readonly List<string> _log = new();
        private int _holdAttempts;

        public volatile bool Idle = true;
        public int HoldAttempts => Volatile.Read(ref _holdAttempts);
        public string[] Log { get { lock (_log) return _log.ToArray(); } }
        public void Add(string entry) { lock (_log) _log.Add(entry); }

        public bool TryHoldIfIdle()
        {
            Interlocked.Increment(ref _holdAttempts);
            Add("hold:" + Idle);
            return Idle;
        }

        public void Release() => Add("release");
    }

    private sealed class FakeUpdate
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public Func<int, Task>? Behavior { get; set; }
        public FakeGate? Gate { get; set; }

        public async Task<EngineUpdateResult> RunAsync(CancellationToken token)
        {
            int n = Interlocked.Increment(ref _calls);
            Gate?.Add("update");
            if (Behavior != null) await Behavior(n);
            return new EngineUpdateResult(EngineUpdateStatus.UpToDate, "1", "1", "ok");
        }
    }

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

    [Fact]
    public async Task An_update_on_demand_holds_the_queue_runs_and_releases_in_that_order()
    {
        var gate = new FakeGate();
        var update = new FakeUpdate { Gate = gate };
        using var scheduler = new EngineUpdateScheduler(gate, update.RunAsync, Hour, Hour);

        var result = await scheduler.RunIfIdleAsync();

        Assert.NotNull(result);
        Assert.Equal(new[] { "hold:True", "update", "release" }, gate.Log);
    }

    [Fact]
    public async Task An_update_on_demand_does_nothing_when_a_download_is_running()
    {
        var gate = new FakeGate { Idle = false };
        var update = new FakeUpdate { Gate = gate };
        using var scheduler = new EngineUpdateScheduler(gate, update.RunAsync, Hour, Hour);

        var result = await scheduler.RunIfIdleAsync();

        Assert.Null(result);
        Assert.Equal(0, update.Calls);
        Assert.DoesNotContain("release", gate.Log);
    }

    [Fact]
    public async Task The_queue_is_released_even_when_the_update_throws()
    {
        var gate = new FakeGate();
        var update = new FakeUpdate { Gate = gate, Behavior = _ => throw new InvalidOperationException("boom") };
        using var scheduler = new EngineUpdateScheduler(gate, update.RunAsync, Hour, Hour);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.RunIfIdleAsync());

        Assert.Equal("release", gate.Log.Last());
    }

    [Fact]
    public async Task The_launch_check_runs_at_once_without_holding_the_queue()
    {
        var gate = new FakeGate();
        var update = new FakeUpdate { Gate = gate };
        using var scheduler = new EngineUpdateScheduler(gate, update.RunAsync, Hour, Hour);

        scheduler.Start();
        await WaitFor(() => update.Calls == 1, "launch check");
        await Settle();

        Assert.Equal(1, update.Calls);
        Assert.Equal(new[] { "update" }, gate.Log);
    }

    [Fact]
    public async Task Starting_twice_runs_one_launch_check()
    {
        var gate = new FakeGate();
        var update = new FakeUpdate { Gate = gate };
        using var scheduler = new EngineUpdateScheduler(gate, update.RunAsync, Hour, Hour);

        scheduler.Start();
        scheduler.Start();
        await WaitFor(() => update.Calls >= 1, "launch check");
        await Settle();

        Assert.Equal(1, update.Calls);
    }

    [Fact]
    public async Task The_check_repeats_after_the_interval()
    {
        var gate = new FakeGate();
        var update = new FakeUpdate { Gate = gate };
        using var scheduler = new EngineUpdateScheduler(gate, update.RunAsync, TimeSpan.FromMilliseconds(30), Hour);

        scheduler.Start();

        await WaitFor(() => update.Calls >= 3, "launch check plus two scheduled checks");
        Assert.Contains("hold:True", gate.Log);
    }

    [Fact]
    public async Task A_busy_queue_is_retried_after_the_short_delay_until_it_is_idle()
    {
        var gate = new FakeGate { Idle = false };
        var update = new FakeUpdate { Gate = gate };
        using var scheduler = new EngineUpdateScheduler(gate, update.RunAsync, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20));

        scheduler.Start();
        await WaitFor(() => gate.HoldAttempts >= 3, "several refused attempts");
        Assert.Equal(1, update.Calls); // only the launch check ran

        gate.Idle = true;
        await WaitFor(() => update.Calls >= 2, "an update once the queue is idle");
    }

    [Fact]
    public async Task A_failing_update_does_not_end_the_daily_loop()
    {
        var gate = new FakeGate();
        var update = new FakeUpdate { Gate = gate, Behavior = n => n == 1 ? throw new InvalidOperationException("boom") : Task.CompletedTask };
        using var scheduler = new EngineUpdateScheduler(gate, update.RunAsync, TimeSpan.FromMilliseconds(30), Hour);

        scheduler.Start();

        await WaitFor(() => update.Calls >= 3, "checks after the failed launch check");
    }

    [Fact]
    public async Task Disposing_stops_the_checks()
    {
        var gate = new FakeGate();
        var update = new FakeUpdate { Gate = gate };
        var scheduler = new EngineUpdateScheduler(gate, update.RunAsync, TimeSpan.FromMilliseconds(20), Hour);
        scheduler.Start();
        await WaitFor(() => update.Calls >= 2, "two checks");

        scheduler.Dispose();
        await Settle();
        int after = update.Calls;
        await Settle();

        Assert.Equal(after, update.Calls);
    }
}

using System.Collections.Concurrent;
using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Phase 3, step 3.3: one instance per user via a lock file and a per-user pipe. Real files and pipes, unique names per test, so it runs on every OS.</summary>
public class SingleInstanceTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ytdlp-single-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _pipe = NewPipe();
    private readonly List<SingleInstance> _made = new();

    private static string NewPipe() => "t-" + Guid.NewGuid().ToString("N")[..12];

    private SingleInstance Make(string? lockDir = null, string? pipe = null)
    {
        var instance = new SingleInstance(lockDir ?? Path.Combine(_root, "lock"), pipe ?? _pipe);
        _made.Add(instance);
        return instance;
    }

    public void Dispose()
    {
        foreach (var instance in _made) instance.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void The_first_instance_becomes_primary_and_a_second_one_does_not()
    {
        var first = Make();
        var second = Make();

        Assert.True(first.TryBecomePrimary());
        Assert.False(second.TryBecomePrimary());
    }

    [Fact]
    public void The_lock_is_free_again_once_the_primary_is_disposed()
    {
        var first = Make();
        Assert.True(first.TryBecomePrimary());
        first.Dispose();

        Assert.True(Make().TryBecomePrimary());
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var first = Make();
        first.TryBecomePrimary();

        first.Dispose();
        first.Dispose();
    }

    [Fact]
    public async Task A_message_reaches_the_primary()
    {
        var primary = Make();
        Assert.True(primary.TryBecomePrimary());
        var got = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.MessageReceived += (_, message) => got.TrySetResult(message);

        Assert.True(await SingleInstance.SendAsync(_pipe, "ytdlp://show"));

        Assert.Equal("ytdlp://show", await got.Task.WaitAsync(Patience));
    }

    [Fact]
    public async Task Messages_sent_one_after_another_all_arrive_in_order()
    {
        var primary = Make();
        Assert.True(primary.TryBecomePrimary());
        var received = new ConcurrentQueue<string>();
        primary.MessageReceived += (_, message) => received.Enqueue(message);

        for (int i = 1; i <= 5; i++) Assert.True(await SingleInstance.SendAsync(_pipe, "m" + i));

        var until = DateTime.UtcNow + Patience;
        while (received.Count < 5 && DateTime.UtcNow < until) await Task.Delay(20);
        Assert.Equal(new[] { "m1", "m2", "m3", "m4", "m5" }, received.ToArray());
    }

    [Fact]
    public async Task Sending_fails_when_nobody_is_listening()
    {
        Assert.False(await SingleInstance.SendAsync(NewPipe(), "ytdlp://show", 300));
    }

    [Theory]
    [InlineData("two\nlines")]
    [InlineData("two\rlines")]
    [InlineData("")]
    public async Task A_message_that_is_empty_or_has_a_line_break_is_refused(string message)
    {
        var primary = Make();
        Assert.True(primary.TryBecomePrimary());

        Assert.False(await SingleInstance.SendAsync(_pipe, message, 300));
    }

    [Fact]
    public async Task Instances_with_different_ids_do_not_see_each_other()
    {
        string otherPipe = NewPipe();
        var a = Make(Path.Combine(_root, "a"), _pipe);
        var b = Make(Path.Combine(_root, "b"), otherPipe);
        Assert.True(a.TryBecomePrimary());
        Assert.True(b.TryBecomePrimary());
        var gotA = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int countB = 0;
        a.MessageReceived += (_, message) => gotA.TrySetResult(message);
        b.MessageReceived += (_, _) => Interlocked.Increment(ref countB);

        Assert.True(await SingleInstance.SendAsync(_pipe, "for-a"));

        Assert.Equal("for-a", await gotA.Task.WaitAsync(Patience));
        await Task.Delay(200);
        Assert.Equal(0, countB);
    }

    [Fact]
    public async Task A_message_that_is_too_long_is_dropped_and_the_listener_keeps_working()
    {
        var primary = Make();
        Assert.True(primary.TryBecomePrimary());
        var received = new ConcurrentQueue<string>();
        var after = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.MessageReceived += (_, message) =>
        {
            received.Enqueue(message);
            if (message == "after") after.TrySetResult(message);
        };

        await SingleInstance.SendAsync(_pipe, new string('x', SingleInstance.MaxMessageChars + 5000), 500);   // the outcome is not the point
        Assert.True(await SingleInstance.SendAsync(_pipe, "after"));

        await after.Task.WaitAsync(Patience);
        Assert.Equal(new[] { "after" }, received.ToArray());
    }

    [Fact]
    public async Task A_handler_that_throws_does_not_stop_the_listener()
    {
        var primary = Make();
        Assert.True(primary.TryBecomePrimary());
        int calls = 0;
        var second = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.MessageReceived += (_, message) =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("boom");
            second.TrySetResult(message);
        };

        Assert.True(await SingleInstance.SendAsync(_pipe, "one"));
        Assert.True(await SingleInstance.SendAsync(_pipe, "two"));

        Assert.Equal("two", await second.Task.WaitAsync(Patience));
    }

    [Fact]
    public void The_pipe_name_is_short_stable_and_different_per_user()
    {
        string alice = SingleInstance.PipeNameForUser("alice");

        Assert.Equal(alice, SingleInstance.PipeNameForUser("alice"));
        Assert.NotEqual(alice, SingleInstance.PipeNameForUser("bob"));
        Assert.True(alice.Length <= 16, alice);   // Unix socket paths are limited to about 100 characters including the temp folder
    }
}

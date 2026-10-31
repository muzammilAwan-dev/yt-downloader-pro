using Xunit;
using YTDLPHost.Native;
using YTDLPHost.Shim;

namespace YTDLPHost.Tests.Native;

/// <summary>Phase 2, step 2.4: the shim's logic (who may start it, and copying frames both ways) over real pipes. Program.cs itself is checked by hand.</summary>
public class ShimBridgeTests
{
    private const string Allowed = "chrome-extension://abcdefghijklmnopabcdefghijklmnop/";

    private sealed class Rig : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(30));
        public PipeRig BrowserIn { get; private set; } = null!;   // the test writes to .Client, the shim reads .Server
        public PipeRig BrowserOut { get; private set; } = null!;  // the shim writes to .Server, the test reads .Client
        public PipeRig App { get; private set; } = null!;         // the shim uses .Client, the test plays the app with .Server
        public Task<ShimBridge.Outcome> Run { get; private set; } = null!;
        public CancellationToken Token => _cts.Token;

        public static async Task<Rig> StartAsync()
        {
            var rig = new Rig { BrowserIn = await PipeRig.CreateAsync(), BrowserOut = await PipeRig.CreateAsync(), App = await PipeRig.CreateAsync() };
            rig.Run = Task.Run(() => ShimBridge.RunAsync(rig.BrowserIn.Server, rig.BrowserOut.Server, rig.App.Client, rig._cts.Token));
            return rig;
        }

        public async Task<ShimBridge.Outcome> OutcomeAsync()
        {
            var finished = await Task.WhenAny(Run, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(Run, finished);
            return await Run;
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            await BrowserIn.DisposeAsync();
            await BrowserOut.DisposeAsync();
            await App.DisposeAsync();
            try { await Run; } catch { /* the bridge reports problems as an outcome; this only guards teardown */ }
            _cts.Dispose();
        }
    }

    private static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    // ------------------------------------------------------------------ copying frames

    [Fact]
    public async Task A_frame_from_the_browser_reaches_the_app_unchanged()
    {
        await using var rig = await Rig.StartAsync();

        await NativeFrame.WriteAsync(rig.BrowserIn.Client, Bytes("{\"hello\":1}"), NativeFrame.MaxBrowserToHostBytes, rig.Token);
        var received = await NativeFrame.ReadAsync(rig.App.Server, NativeFrame.MaxBrowserToHostBytes, rig.Token);

        Assert.Equal(Bytes("{\"hello\":1}"), received);
    }

    [Fact]
    public async Task Frames_from_the_app_reach_the_browser_unchanged_and_in_order()
    {
        await using var rig = await Rig.StartAsync();

        // Named pipes here have no buffer, so a write only completes when the other end reads: write and read at the same time,
        // as the app and the browser do in real life (writing all three first would wait for a reader that is not there yet).
        var writer = Task.Run(async () =>
        {
            foreach (var text in new[] { "one", "two", "three" })
                await NativeFrame.WriteAsync(rig.App.Server, Bytes(text), NativeFrame.MaxHostToBrowserBytes, rig.Token);
        });

        foreach (var text in new[] { "one", "two", "three" })
            Assert.Equal(Bytes(text), await NativeFrame.ReadAsync(rig.BrowserOut.Client, NativeFrame.MaxHostToBrowserBytes, rig.Token));
        await writer;
    }

    [Fact]
    public async Task Both_directions_work_at_the_same_time()
    {
        await using var rig = await Rig.StartAsync();

        // Unbuffered pipes: every write below waits for its reader, so each direction gets its own writer task.
        var fromBrowser = NativeFrame.WriteAsync(rig.BrowserIn.Client, Bytes("request"), NativeFrame.MaxBrowserToHostBytes, rig.Token);
        var fromApp = Task.Run(async () =>
        {
            await NativeFrame.WriteAsync(rig.App.Server, Bytes("progress"), NativeFrame.MaxHostToBrowserBytes, rig.Token);
            await NativeFrame.WriteAsync(rig.App.Server, Bytes("done"), NativeFrame.MaxHostToBrowserBytes, rig.Token);
        });

        Assert.Equal(Bytes("request"), await NativeFrame.ReadAsync(rig.App.Server, NativeFrame.MaxBrowserToHostBytes, rig.Token));
        Assert.Equal(Bytes("progress"), await NativeFrame.ReadAsync(rig.BrowserOut.Client, NativeFrame.MaxHostToBrowserBytes, rig.Token));
        Assert.Equal(Bytes("done"), await NativeFrame.ReadAsync(rig.BrowserOut.Client, NativeFrame.MaxHostToBrowserBytes, rig.Token));
        await fromBrowser;
        await fromApp;
    }

    [Fact]
    public async Task A_frame_much_larger_than_the_pipe_buffer_survives_in_both_directions()
    {
        await using var rig = await Rig.StartAsync();
        var big = new byte[900 * 1024];
        new Random(7).NextBytes(big);

        var send = NativeFrame.WriteAsync(rig.BrowserIn.Client, big, NativeFrame.MaxBrowserToHostBytes, rig.Token);
        var atApp = await NativeFrame.ReadAsync(rig.App.Server, NativeFrame.MaxBrowserToHostBytes, rig.Token);
        await send;

        var back = NativeFrame.WriteAsync(rig.App.Server, big, NativeFrame.MaxHostToBrowserBytes, rig.Token);
        var atBrowser = await NativeFrame.ReadAsync(rig.BrowserOut.Client, NativeFrame.MaxHostToBrowserBytes, rig.Token);
        await back;

        Assert.Equal(big, atApp);
        Assert.Equal(big, atBrowser);
    }

    // ------------------------------------------------------------------ the end of the bridge

    [Fact]
    public async Task The_bridge_ends_when_the_browser_closes()
    {
        await using var rig = await Rig.StartAsync();

        await rig.BrowserIn.Client.DisposeAsync();

        Assert.Equal(ShimEnd.BrowserClosed, (await rig.OutcomeAsync()).End);
    }

    [Fact]
    public async Task The_bridge_ends_when_the_app_closes()
    {
        await using var rig = await Rig.StartAsync();

        await rig.App.Server.DisposeAsync();

        Assert.Equal(ShimEnd.AppClosed, (await rig.OutcomeAsync()).End);
    }

    [Fact]
    public async Task A_browser_frame_over_the_limit_ends_the_bridge_with_an_error()
    {
        await using var rig = await Rig.StartAsync();

        await rig.BrowserIn.Client.WriteAsync(BitConverter.GetBytes(uint.MaxValue), rig.Token);
        await rig.BrowserIn.Client.FlushAsync(rig.Token);

        Assert.Equal(ShimEnd.Error, (await rig.OutcomeAsync()).End);
    }

    [Fact]
    public async Task An_app_frame_over_the_browser_limit_ends_the_bridge_with_an_error()
    {
        await using var rig = await Rig.StartAsync();

        await rig.App.Server.WriteAsync(BitConverter.GetBytes((uint)(NativeFrame.MaxHostToBrowserBytes + 1)), rig.Token);
        await rig.App.Server.FlushAsync(rig.Token);

        Assert.Equal(ShimEnd.Error, (await rig.OutcomeAsync()).End);
    }

    [Fact]
    public async Task A_truncated_browser_frame_ends_the_bridge_with_an_error()
    {
        await using var rig = await Rig.StartAsync();

        await rig.BrowserIn.Client.WriteAsync(BitConverter.GetBytes(100u), rig.Token);
        await rig.BrowserIn.Client.WriteAsync(new byte[10], rig.Token);
        await rig.BrowserIn.Client.FlushAsync(rig.Token);
        await rig.BrowserIn.Client.DisposeAsync();

        Assert.Equal(ShimEnd.Error, (await rig.OutcomeAsync()).End);
    }

    // ------------------------------------------------------------------ who may start the shim

    [Fact]
    public void The_allowed_origins_file_ignores_comments_blank_lines_and_line_endings()
    {
        var parsed = ShimOrigin.ParseAllowed("# the extension\r\n" + Allowed + "\r\n\r\n   \r\n  chrome-extension://second/  \r\n");

        Assert.Equal(new[] { Allowed, "chrome-extension://second/" }, parsed);
    }

    [Theory]
    [InlineData(Allowed, true)]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnop/ --parent-window=1234", false)] // one argument that merely starts with it
    [InlineData("chrome-extension://zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz/", false)]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnop/evil", false)]
    [InlineData("CHROME-EXTENSION://ABCDEFGHIJKLMNOPABCDEFGHIJKLMNOP/", false)]
    [InlineData("", false)]
    public void Only_the_exact_allowed_origin_may_start_the_shim(string firstArgument, bool expected)
    {
        Assert.Equal(expected, ShimOrigin.IsAllowed(new[] { firstArgument }, new[] { Allowed }));
    }

    [Fact]
    public void Chromes_extra_windows_argument_after_the_origin_is_fine()
    {
        Assert.True(ShimOrigin.IsAllowed(new[] { Allowed, "--parent-window=1234" }, new[] { Allowed }));
    }

    [Fact]
    public void No_arguments_or_no_allowed_origins_means_nobody_may_start_the_shim()
    {
        Assert.False(ShimOrigin.IsAllowed(Array.Empty<string>(), new[] { Allowed }));
        Assert.False(ShimOrigin.IsAllowed(new[] { Allowed }, Array.Empty<string>()));
    }
}

using System.IO.Pipes;
using Xunit;
using YTDLPHost.Native;

namespace YTDLPHost.Tests.Native;

/// <summary>Phase 2, step 2.3: the accepting side, over a real named pipe (the same code the app runs).</summary>
public class NativePipeServerTests
{
    private static NativePipeServer StartServer(string name) =>
        Started(new NativePipeServer(name, new FakeNativeSink(), () => new MapperSettings(""), "9.9.9", TimeSpan.FromMilliseconds(10)));

    private static NativePipeServer Started(NativePipeServer server)
    {
        server.Start();
        return server;
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(string name)
    {
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, NativePipe.ClientOptions);
        await client.ConnectAsync(10000);
        return client;
    }

    [Fact]
    public async Task A_client_can_say_hello_over_the_real_pipe()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        string name = "ytdlp-test-" + Guid.NewGuid().ToString("N");
        using var server = StartServer(name);
        await using var client = await ConnectAsync(name);

        await NativeWire.SendAsync(client, NativeWire.HelloJson("p1"), cts.Token);
        var reply = await NativeWire.ReadAsync(client, cts.Token);

        Assert.Equal("hello", reply.GetProperty("type").GetString());
        Assert.Equal("p1", reply.GetProperty("id").GetString());
        Assert.Equal("9.9.9", reply.GetProperty("hostVersion").GetString());
    }

    [Fact]
    public async Task Two_clients_are_served_at_the_same_time()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        string name = "ytdlp-test-" + Guid.NewGuid().ToString("N");
        using var server = StartServer(name);
        await using var first = await ConnectAsync(name);
        await using var second = await ConnectAsync(name);

        await NativeWire.SendAsync(first, NativeWire.HelloJson("a"), cts.Token);
        await NativeWire.SendAsync(second, NativeWire.HelloJson("b"), cts.Token);
        var replyFirst = await NativeWire.ReadAsync(first, cts.Token);
        var replySecond = await NativeWire.ReadAsync(second, cts.Token);

        Assert.Equal("a", replyFirst.GetProperty("id").GetString());
        Assert.Equal("b", replySecond.GetProperty("id").GetString());
    }

    [Fact]
    public void The_pipe_name_is_per_user_and_uses_only_safe_characters()
    {
        string name = NativePipe.NameForCurrentUser();

        Assert.StartsWith("YTDLPHost_Native_", name);
        Assert.All(name, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'));
        Assert.True(name.Length <= 17 + 32);
    }
}

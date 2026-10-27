using System.Text.Json;
using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Native;

namespace YTDLPHost.Tests.Native;

/// <summary>
/// Phase 2, step 2.3: one shim connection, driven over a real pipe exactly as the shim will drive it. The sink is a fake, so these tests
/// cover the request handling and the progress reporting, not the download list (see NativeDownloadSinkTests).
/// </summary>
public class NativeConnectionTests
{
    private sealed class Rig : IAsyncDisposable
    {
        private readonly PipeRig _pipes;
        private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(30));

        public FakeNativeSink Sink { get; } = new();
        public Task Run { get; }
        public Stream Client => _pipes.Client;
        public CancellationToken Token => _cts.Token;

        private Rig(PipeRig pipes, string folder)
        {
            _pipes = pipes;
            var connection = new NativeConnection(pipes.Server, Sink, () => new MapperSettings(folder), "1.2.3", TimeSpan.FromMilliseconds(10));
            Run = Task.Run(() => connection.RunAsync(_cts.Token));
        }

        public static async Task<Rig> StartAsync(string folder = "") => new(await PipeRig.CreateAsync(), folder);

        public Task SendAsync(string json) => NativeWire.SendAsync(Client, json, Token);
        public Task<JsonElement> NextAsync() => NativeWire.ReadAsync(Client, Token);
        public Task<JsonElement> UntilAsync(string type) => NativeWire.UntilTypeAsync(Client, type, Token);

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            await _pipes.DisposeAsync();
            try { await Run; } catch { /* the connection never throws; this only guards the test teardown */ }
            _cts.Dispose();
        }
    }

    // ------------------------------------------------------------------ hello and refusals

    [Fact]
    public async Task Hello_is_answered_with_the_host_version_and_the_protocol()
    {
        await using var rig = await Rig.StartAsync();

        await rig.SendAsync(NativeWire.HelloJson("h7"));
        var reply = await rig.NextAsync();

        Assert.Equal("hello", reply.GetProperty("type").GetString());
        Assert.Equal("h7", reply.GetProperty("id").GetString());
        Assert.Equal("1.2.3", reply.GetProperty("hostVersion").GetString());
        Assert.Equal(1, reply.GetProperty("protocol").GetInt32());
    }

    [Fact]
    public async Task Garbage_is_rejected_and_the_connection_stays_usable()
    {
        await using var rig = await Rig.StartAsync();

        await rig.SendAsync("this is not json");
        var rejected = await rig.NextAsync();
        await rig.SendAsync(NativeWire.HelloJson());
        var hello = await rig.NextAsync();

        Assert.Equal("rejected", rejected.GetProperty("type").GetString());
        Assert.Equal("bad_json", rejected.GetProperty("code").GetString());
        Assert.False(rejected.TryGetProperty("id", out _));
        Assert.Equal("hello", hello.GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_wrong_protocol_version_is_rejected_with_the_request_id()
    {
        await using var rig = await Rig.StartAsync();

        await rig.SendAsync("{\"v\":2,\"id\":\"x1\",\"type\":\"hello\"}");
        var rejected = await rig.NextAsync();

        Assert.Equal("rejected", rejected.GetProperty("type").GetString());
        Assert.Equal("bad_version", rejected.GetProperty("code").GetString());
        Assert.Equal("x1", rejected.GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_request_the_mapper_refuses_is_rejected_and_nothing_is_queued()
    {
        await using var rig = await Rig.StartAsync();

        await rig.SendAsync(NativeWire.DownloadJson("c1", "\"quality\":\"custom\",\"custom\":\"--exec calc\""));
        var rejected = await rig.NextAsync();

        Assert.Equal("rejected", rejected.GetProperty("type").GetString());
        Assert.Equal("bad_field", rejected.GetProperty("code").GetString());
        Assert.Equal("c1", rejected.GetProperty("id").GetString());
        Assert.Empty(rig.Sink.Received);
    }

    // ------------------------------------------------------------------ accepting a download

    [Fact]
    public async Task A_valid_download_is_mapped_queued_and_accepted()
    {
        await using var rig = await Rig.StartAsync();

        await rig.SendAsync(NativeWire.DownloadJson("d1", "\"quality\":\"720\"", "https://vimeo.com/55"));
        var accepted = await rig.NextAsync();

        Assert.Equal("accepted", accepted.GetProperty("type").GetString());
        Assert.Equal("d1", accepted.GetProperty("id").GetString());
        var download = Assert.Single(rig.Sink.Received);
        Assert.StartsWith("yt-dlp ", download.Command);
        Assert.Contains("https://vimeo.com/55", download.Command);
    }

    [Fact]
    public async Task The_save_folder_comes_from_the_host_settings()
    {
        await using var rig = await Rig.StartAsync(@"D:\Videos");

        await rig.SendAsync(NativeWire.DownloadJson());
        await rig.UntilAsync("accepted");

        Assert.Contains("D:/Videos/", Assert.Single(rig.Sink.Received).Command);
    }

    [Fact]
    public async Task When_the_app_cannot_take_downloads_the_browser_gets_an_error_and_no_accepted()
    {
        await using var rig = await Rig.StartAsync();
        rig.Sink.Unavailable = "setup failed";

        await rig.SendAsync(NativeWire.DownloadJson("d9"));
        var reply = await rig.NextAsync();

        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("d9", reply.GetProperty("id").GetString());
        Assert.Equal("setup failed", reply.GetProperty("reason").GetString());
    }

    // ------------------------------------------------------------------ progress and the end of a download

    [Fact]
    public async Task Progress_is_streamed_and_a_finished_download_ends_with_done_and_its_note()
    {
        await using var rig = await Rig.StartAsync();
        await rig.SendAsync(NativeWire.DownloadJson());
        await rig.UntilAsync("accepted");

        var task = rig.Sink.Items[0].Task;
        // Progress last: a frame can only show 40 once the speed and eta are already in place.
        task.Status = DownloadStatus.Downloading;
        task.Speed = "2.0MiB/s";
        task.Eta = "00:10";
        task.Progress = 40;

        JsonElement progress;
        do { progress = await rig.UntilAsync("progress"); }
        while (progress.GetProperty("percent").GetDouble() < 40);

        Assert.Equal(40, progress.GetProperty("percent").GetDouble());
        Assert.Equal("2.0MiB/s", progress.GetProperty("speed").GetString());
        Assert.Equal("00:10", progress.GetProperty("eta").GetString());

        task.Progress = 100;
        task.ResultNote = "file already existed, nothing new downloaded";
        task.Status = DownloadStatus.Completed;

        var done = await rig.UntilAsync("done");
        Assert.Equal("d1", done.GetProperty("id").GetString());
        Assert.Equal("file already existed, nothing new downloaded", done.GetProperty("note").GetString());
    }

    [Fact]
    public async Task A_failed_download_ends_with_error_and_the_reason()
    {
        await using var rig = await Rig.StartAsync();
        await rig.SendAsync(NativeWire.DownloadJson());
        await rig.UntilAsync("accepted");

        var task = rig.Sink.Items[0].Task;
        task.ErrorMessage = "Video unavailable";
        task.Status = DownloadStatus.Error;

        var error = await rig.UntilAsync("error");
        Assert.Equal("Video unavailable", error.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_download_the_user_removed_from_the_list_ends_with_error()
    {
        await using var rig = await Rig.StartAsync();
        await rig.SendAsync(NativeWire.DownloadJson());
        await rig.UntilAsync("accepted");

        rig.Sink.RemoveFromList(rig.Sink.Items[0]);

        var error = await rig.UntilAsync("error");
        Assert.Contains("removed", error.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Several_urls_report_one_done_when_every_download_finished()
    {
        await using var rig = await Rig.StartAsync();
        await rig.SendAsync(NativeWire.DownloadJson("m1", "\"quality\":\"720\"", "https://vimeo.com/1", "https://vimeo.com/2"));
        await rig.UntilAsync("accepted");
        Assert.Equal(2, rig.Sink.Items.Count);

        rig.Sink.Items[0].Task.Status = DownloadStatus.Completed;
        rig.Sink.Items[1].Task.Status = DownloadStatus.Completed;

        var done = await rig.UntilAsync("done");
        Assert.Equal("m1", done.GetProperty("id").GetString());
        Assert.False(done.TryGetProperty("note", out _));
    }

    [Fact]
    public async Task Several_urls_with_one_failure_report_done_with_a_note()
    {
        await using var rig = await Rig.StartAsync();
        await rig.SendAsync(NativeWire.DownloadJson("m2", "\"quality\":\"720\"", "https://vimeo.com/1", "https://vimeo.com/2"));
        await rig.UntilAsync("accepted");

        rig.Sink.Items[0].Task.Status = DownloadStatus.Completed;
        rig.Sink.Items[1].Task.ErrorMessage = "boom";
        rig.Sink.Items[1].Task.Status = DownloadStatus.Error;

        var done = await rig.UntilAsync("done");
        Assert.Equal("1 finished, 1 failed: boom", done.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Several_urls_that_all_failed_report_an_error()
    {
        await using var rig = await Rig.StartAsync();
        await rig.SendAsync(NativeWire.DownloadJson("m3", "\"quality\":\"720\"", "https://vimeo.com/1", "https://vimeo.com/2"));
        await rig.UntilAsync("accepted");

        foreach (var item in rig.Sink.Items)
        {
            item.Task.ErrorMessage = "boom";
            item.Task.Status = DownloadStatus.Error;
        }

        var error = await rig.UntilAsync("error");
        Assert.Equal("2 of 2 downloads failed: boom", error.GetProperty("reason").GetString());
    }

    // ------------------------------------------------------------------ the end of a connection

    [Fact]
    public async Task The_connection_ends_when_the_shim_disconnects_and_the_download_carries_on()
    {
        var rig = await Rig.StartAsync();
        await rig.SendAsync(NativeWire.DownloadJson());
        await rig.UntilAsync("accepted");
        var item = rig.Sink.Items[0];
        item.Task.Status = DownloadStatus.Downloading;

        await rig.Client.DisposeAsync();
        var finished = await Task.WhenAny(rig.Run, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(rig.Run, finished);
        Assert.Equal(DownloadStatus.Downloading, item.Task.Status);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task A_message_larger_than_the_limit_ends_the_connection()
    {
        await using var rig = await Rig.StartAsync();

        await rig.Client.WriteAsync(BitConverter.GetBytes(uint.MaxValue), rig.Token);
        await rig.Client.FlushAsync(rig.Token);
        var finished = await Task.WhenAny(rig.Run, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(rig.Run, finished);
    }
}

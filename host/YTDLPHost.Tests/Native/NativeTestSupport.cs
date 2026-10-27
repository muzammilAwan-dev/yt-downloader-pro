using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Native;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Tests.Native;

/// <summary>A sink that records what it was given and lets a test move the items through their states.</summary>
internal sealed class FakeNativeSink : INativeDownloadSink
{
    private readonly object _gate = new();
    private readonly HashSet<Guid> _listed = new();

    public readonly List<MappedDownload> Received = new();
    public readonly List<DownloadItemViewModel> Items = new();

    /// <summary>When set, Submit refuses everything with this text.</summary>
    public string? Unavailable;

    public NativeSubmission Submit(IReadOnlyList<MappedDownload> downloads)
    {
        lock (_gate)
        {
            if (Unavailable != null) return new NativeSubmission(Array.Empty<DownloadItemViewModel>(), Unavailable);

            var added = new List<DownloadItemViewModel>();
            foreach (var download in downloads)
            {
                Received.Add(download);
                var item = new DownloadItemViewModel(new DownloadTask { Command = download.Command, Status = DownloadStatus.Queued });
                Items.Add(item);
                added.Add(item);
                _listed.Add(item.Id);
            }
            return new NativeSubmission(added, null);
        }
    }

    public HashSet<Guid> ListedIds()
    {
        lock (_gate) return new HashSet<Guid>(_listed);
    }

    public void RemoveFromList(DownloadItemViewModel item)
    {
        lock (_gate) _listed.Remove(item.Id);
    }
}

/// <summary>A connected pair of real named pipe streams (works on Windows and Linux).</summary>
internal sealed class PipeRig : IAsyncDisposable
{
    public NamedPipeServerStream Server { get; }
    public NamedPipeClientStream Client { get; }

    private PipeRig(NamedPipeServerStream server, NamedPipeClientStream client)
    {
        Server = server;
        Client = client;
    }

    public static async Task<PipeRig> CreateAsync()
    {
        string name = "ytdlp-test-" + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(), client.ConnectAsync(5000));
        return new PipeRig(server, client);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Server.DisposeAsync();
    }
}

/// <summary>Helpers shared by the tests that talk to a <see cref="NativeConnection"/> or a <see cref="NativePipeServer"/> as the browser would.</summary>
internal static class NativeWire
{
    public static string HelloJson(string id = "h1") => $"{{\"v\":1,\"id\":\"{id}\",\"type\":\"hello\"}}";

    public static string DownloadJson(string id = "d1", string options = "\"quality\":\"1080\"", params string[] urls)
    {
        var list = string.Join(",", (urls.Length == 0 ? new[] { "https://vimeo.com/123456789" } : urls).Select(u => $"\"{u}\""));
        return $"{{\"v\":1,\"id\":\"{id}\",\"type\":\"download\",\"urls\":[{list}],\"options\":{{{options}}}}}";
    }

    public static Task SendAsync(Stream stream, string json, CancellationToken cancellationToken) =>
        NativeFrame.WriteAsync(stream, Encoding.UTF8.GetBytes(json), NativeFrame.MaxHostToBrowserBytes, cancellationToken);

    public static async Task<JsonElement> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = await NativeFrame.ReadAsync(stream, NativeFrame.MaxHostToBrowserBytes, cancellationToken);
        Assert.NotNull(bytes);
        using var document = JsonDocument.Parse(bytes!);
        return document.RootElement.Clone();
    }

    /// <summary>Reads messages until one has the given type (progress frames in between are skipped).</summary>
    public static async Task<JsonElement> UntilTypeAsync(Stream stream, string type, CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await ReadAsync(stream, cancellationToken);
            if (message.GetProperty("type").GetString() == type) return message;
        }
    }
}

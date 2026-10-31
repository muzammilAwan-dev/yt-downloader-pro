using System.Collections.Concurrent;
using System.IO.Pipes;
using YTDLPHost.Services;

namespace YTDLPHost.Native
{
    /// <summary>
    /// Accepts shim connections on a named pipe and serves each with a <see cref="NativeConnection"/> (Phase 2, step 2.3). Portable:
    /// .NET maps the pipe to a Unix socket on Linux and macOS. At most <see cref="MaxConnections"/> shims are served at once.
    /// </summary>
    public sealed class NativePipeServer : IDisposable
    {
        public const int MaxConnections = 8;

        private readonly string _pipeName;
        private readonly INativeDownloadSink _sink;
        private readonly Func<MapperSettings> _settings;
        private readonly string _hostVersion;
        private readonly TimeSpan? _pollInterval;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<Task, byte> _serving = new();
        private Task? _acceptLoop;
        private int _disposed;

        public NativePipeServer(string pipeName, INativeDownloadSink sink, Func<MapperSettings> settings, string hostVersion, TimeSpan? pollInterval = null)
        {
            _pipeName = pipeName;
            _sink = sink;
            _settings = settings;
            _hostVersion = hostVersion;
            _pollInterval = pollInterval;
        }

        public void Start()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(NativePipeServer));
            if (_acceptLoop != null) throw new InvalidOperationException("The server was already started.");
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
            AppLogger.Log("[NATIVE] Pipe server started.");
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, MaxConnections, PipeTransmissionMode.Byte, NativePipe.ServerOptions);
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    pipe?.Dispose();
                    break;
                }
                catch (Exception ex)
                {
                    // For example "all pipe instances are busy": wait a moment and try again.
                    pipe?.Dispose();
                    AppLogger.Log($"[NATIVE] Pipe accept failed: {ex.Message}");
                    try { await Task.Delay(1000, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                var connected = pipe!;
                var serving = Task.Run(() => ServeAsync(connected, cancellationToken));
                _serving[serving] = 0;
                _ = serving.ContinueWith(t => _serving.TryRemove(t, out _), TaskScheduler.Default);
            }
        }

        private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
        {
            try
            {
                await using (pipe.ConfigureAwait(false))
                {
                    var connection = new NativeConnection(pipe, _sink, _settings, _hostVersion, _pollInterval);
                    await connection.RunAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[NATIVE] A connection ended with an error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            _cts.Cancel();
            try
            {
                var all = _serving.Keys.ToList();
                if (_acceptLoop != null) all.Add(_acceptLoop);
                // Short wait: a connection may be waiting for the UI thread, and this is usually called FROM the UI thread during exit.
                Task.WaitAll(all.ToArray(), TimeSpan.FromSeconds(1));
            }
            catch { /* shutting down */ }
            _cts.Dispose();
        }
    }
}

using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Phase 3, step 3.3: one running app per user, on every OS. Replaces the WPF SingleInstanceManager, whose machine-wide "Global\" mutex was Windows-only and
    /// also kept a second Windows user from starting the app at all.
    ///
    /// The first process locks a file (FileShare.None: the OS drops the lock when the process dies, so there is nothing stale to clean up) and listens on a named
    /// pipe whose name contains a hash of the user name; a later process fails to get the lock and sends its message to that pipe instead. The pipe is
    /// CurrentUserOnly (an ACL on Windows, a 0600 socket plus an owner check on Unix), so another user can neither listen on it nor talk to it.
    /// Messages are single lines of at most <see cref="MaxMessageChars"/> characters. Today that is only the "wake up" signal; the real work travels as a
    /// payload file (see App.OnStartup).
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        public const int MaxMessageChars = 16 * 1024;
        private const string LockFileName = "instance.lock";

        private readonly string _lockDir;
        private readonly string _pipeName;
        private FileStream? _lock;
        private CancellationTokenSource? _cts;
        private Task? _listener;

        /// <param name="lockDir">A folder only this user can write, where the lock file lives.</param>
        /// <param name="pipeName">The pipe the first process listens on; one per user (see <see cref="PipeNameForUser"/>). Kept short: Unix socket paths have a length limit.</param>
        public SingleInstance(string lockDir, string pipeName)
        {
            _lockDir = lockDir;
            _pipeName = pipeName;
        }

        /// <summary>Raised on a thread-pool thread for every message the first process receives. An exception thrown by a handler is swallowed.</summary>
        public event EventHandler<string>? MessageReceived;

        public static string PipeNameForUser(string userName)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(userName ?? ""));
            return "ytdlp-" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
        }

        public static SingleInstance ForCurrentUser(IAppPaths paths) => new(paths.DataDir, PipeNameForUser(Environment.UserName));

        public static Task<bool> SendToRunningInstanceAsync(string message, int timeoutMs = 3000) =>
            SendAsync(PipeNameForUser(Environment.UserName), message, timeoutMs);

        /// <summary>True when this process is the first one (and now listens); false when another process already holds the lock.</summary>
        public bool TryBecomePrimary()
        {
            if (_lock != null) return true;

            try
            {
                Directory.CreateDirectory(_lockDir);
                _lock = new FileStream(Path.Combine(_lockDir, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return false;   // another process holds the lock
            }

            // We hold the lock, so no live server can own the socket file of an earlier crashed run: remove it, or the new server cannot bind.
            if (!OperatingSystem.IsWindows())
            {
                try { File.Delete(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + _pipeName)); } catch (Exception) { }
            }

            _cts = new CancellationTokenSource();
            _listener = Task.Run(() => ListenAsync(_cts.Token));
            return true;
        }

        /// <summary>Sends one line to the first process, retrying for up to <paramref name="timeoutMs"/> (the server is briefly absent between two connections).</summary>
        public static async Task<bool> SendAsync(string pipeName, string message, int timeoutMs = 3000)
        {
            if (string.IsNullOrEmpty(message) || message.IndexOfAny(new[] { '\r', '\n' }) >= 0) return false;

            var clock = Stopwatch.StartNew();
            while (true)
            {
                int remaining = timeoutMs - (int)clock.ElapsedMilliseconds;
                if (remaining <= 0) return false;

                try
                {
                    await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await client.ConnectAsync(remaining);
                    await using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
                    await writer.WriteLineAsync(message);
                    return true;
                }
                catch (Exception)
                {
                    await Task.Delay(Math.Min(50, Math.Max(1, remaining)));
                }
            }
        }

        private async Task ListenAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(ct);

                    // A client that connects and then says nothing must not block the next one for ever.
                    using var readLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readLimit.CancelAfter(TimeSpan.FromSeconds(5));
                    string? message = await ReadMessageAsync(server, readLimit.Token);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        try { MessageReceived?.Invoke(this, message.Trim()); } catch (Exception) { /* a faulty handler must not stop the listener */ }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    try { await Task.Delay(100, ct); } catch (OperationCanceledException) { break; }
                }
            }
        }

        /// <summary>One line, or null when it is longer than <see cref="MaxMessageChars"/> (the rest is discarded with the connection).</summary>
        private static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken ct)
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 1024, leaveOpen: true);
            var text = new StringBuilder();
            var one = new char[1];
            while (true)
            {
                int n = await reader.ReadAsync(one.AsMemory(), ct);
                if (n == 0 || one[0] == '\n') return text.ToString();
                if (one[0] == '\r') continue;
                if (text.Length >= MaxMessageChars) return null;
                text.Append(one[0]);
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try { _listener?.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
            _cts?.Dispose();
            _cts = null;
            _lock?.Dispose();   // the lock file itself stays: deleting it would race with a process that just opened it
            _lock = null;
        }
    }
}

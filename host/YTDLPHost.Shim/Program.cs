using System.Diagnostics;
using System.IO.Pipes;
using YTDLPHost.Native;

namespace YTDLPHost.Shim
{
    /// <summary>
    /// Exit codes (the browser only shows "native host has exited"; the reason is in shim.log):
    /// 0 normal end, 2 the caller is not an allowed extension, 3 the app could not be reached or started, 4 the app refused the connection.
    /// RULE: nothing but protocol frames may ever be written to stdout.
    /// </summary>
    internal static class Program
    {
        private static readonly TimeSpan AppStartTimeout = TimeSpan.FromSeconds(20);

        private static async Task<int> Main(string[] args)
        {
            // Take the raw streams first, then make Console.Out harmless: a stray Console.WriteLine must never corrupt the framing.
            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();
            Console.SetOut(TextWriter.Null);

            ShimLog.Start();
            try
            {
                string originsFile = Path.Combine(AppContext.BaseDirectory, "allowed-origins.txt");
                var allowed = File.Exists(originsFile) ? ShimOrigin.ParseAllowed(File.ReadAllText(originsFile)) : new List<string>();
                if (!ShimOrigin.IsAllowed(args, allowed))
                {
                    ShimLog.Write($"Refused: caller '{(args.Length > 0 ? args[0] : "(none)")}' is not in allowed-origins.txt ({allowed.Count} entries).");
                    return 2;
                }

                using var cts = new CancellationTokenSource();
                var (app, failure) = await ConnectAsync(NativePipe.NameForCurrentUser(), cts.Token);
                if (app == null)
                {
                    ShimLog.Write("Could not reach the app: " + failure);
                    return failure.StartsWith("refused", StringComparison.Ordinal) ? 4 : 3;
                }

                using (app)
                {
                    var outcome = await ShimBridge.RunAsync(stdin, stdout, app, cts.Token);
                    ShimLog.Write($"Bridge ended: {outcome.End}{(outcome.Detail != null ? " (" + outcome.Detail + ")" : "")}");
                }
                return 0;
            }
            catch (Exception ex)
            {
                ShimLog.Write("Unexpected failure: " + ex);
                return 3;
            }
        }

        /// <summary>Connects to the app's pipe; if the app is not running, starts it and waits for the pipe.</summary>
        private static async Task<(NamedPipeClientStream? Pipe, string Failure)> ConnectAsync(string pipeName, CancellationToken cancellationToken)
        {
            var first = await TryConnectAsync(pipeName, 300, cancellationToken);
            if (first.Pipe != null) return (first.Pipe, "");
            if (first.Refused != null) return (null, "refused: " + first.Refused);

            if (!StartApp(out string startFailure)) return (null, startFailure);

            var deadline = DateTime.UtcNow + AppStartTimeout;
            while (DateTime.UtcNow < deadline)
            {
                var attempt = await TryConnectAsync(pipeName, 500, cancellationToken);
                if (attempt.Pipe != null) return (attempt.Pipe, "");
                if (attempt.Refused != null) return (null, "refused: " + attempt.Refused);
                await Task.Delay(250, cancellationToken);
            }
            return (null, $"the app did not open its pipe within {AppStartTimeout.TotalSeconds:0} seconds");
        }

        private static async Task<(NamedPipeClientStream? Pipe, string? Refused)> TryConnectAsync(string pipeName, int timeoutMs, CancellationToken cancellationToken)
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, NativePipe.ClientOptions);
            try
            {
                await pipe.ConnectAsync(timeoutMs, cancellationToken);
                return (pipe, null);
            }
            catch (UnauthorizedAccessException ex)
            {
                // Same name, different user or elevation level (for example the app runs as administrator). Starting another copy would not help.
                pipe.Dispose();
                return (null, "the pipe belongs to a different user or elevation level (" + ex.Message + ")");
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                pipe.Dispose();
                return (null, null);
            }
        }

        private static bool StartApp(out string failure)
        {
            string appPath = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "YTDLPHost.exe" : "YTDLPHost");
            if (!File.Exists(appPath))
            {
                failure = $"the app is not running and '{appPath}' does not exist";
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = appPath, WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false })?.Dispose();
                ShimLog.Write("The app was not running; started " + appPath);
                failure = "";
                return true;
            }
            catch (Exception ex)
            {
                failure = "the app could not be started: " + ex.Message;
                return false;
            }
        }
    }

    /// <summary>A small log file, because stdout belongs to the protocol. Trimmed at start so it cannot grow without bound; never throws.</summary>
    internal static class ShimLog
    {
        private const long MaxBytes = 200 * 1024;
        private static string? _path;

        public static void Start()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YTDownloaderProEngine");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "shim.log");
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes) File.Delete(_path);
            }
            catch { _path = null; }
        }

        public static void Write(string message)
        {
            if (_path == null) return;
            try { File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [pid {Environment.ProcessId}] {message}{Environment.NewLine}"); }
            catch { /* logging must never break the bridge */ }
        }
    }
}

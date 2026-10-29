using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YTDLPHost.Services
{
    public enum EngineUpdateStatus { UpToDate, Updated, Failed, AlreadyRunning }

    /// <summary>Outcome of one "yt-dlp --update-to" run. <see cref="Message"/> is plain language, safe to show to the user.</summary>
    public sealed record EngineUpdateResult(EngineUpdateStatus Status, string? VersionBefore, string? VersionAfter, string Message)
    {
        /// <summary>The text for the "Update engine" dialog: the result, the versions, and the troubleshooting tip.</summary>
        public string ToDialogText()
        {
            var text = Message;
            if (Status == EngineUpdateStatus.Updated && VersionBefore != null && VersionAfter != null)
                text += $"{Environment.NewLine}{Environment.NewLine}Previous version: {VersionBefore}{Environment.NewLine}Current version: {VersionAfter}";
            else if (Status != EngineUpdateStatus.Failed && VersionAfter != null)
                text += $"{Environment.NewLine}{Environment.NewLine}Current version: {VersionAfter}";

            return text + Environment.NewLine + Environment.NewLine + EngineUpdater.TroubleshootingHint;
        }
    }

    /// <summary>
    /// Updates the downloader engine (yt-dlp) in place with its own "--update-to &lt;channel&gt;" and reports what happened.
    /// Used for the check at launch, the daily re-check while the app stays open, and the manual "Update engine" menu item.
    /// Only one update runs at a time; a second call returns <see cref="EngineUpdateStatus.AlreadyRunning"/> at once.
    /// The class knows nothing about the download queue: whoever calls it decides when it is safe to replace the engine file.
    /// </summary>
    public sealed class EngineUpdater
    {
        /// <summary>How often the app re-checks while it stays open (it also checks once at every launch).</summary>
        public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

        /// <summary>When a scheduled check finds downloads running, it tries again after this long.</summary>
        public static readonly TimeSpan BusyRetryInterval = TimeSpan.FromMinutes(15);

        public const string TroubleshootingHint =
            "Tip: if downloads fail with unusual errors (a video that used to work now fails, only low quality is offered, or sites change " +
            "and extraction errors appear), update the engine and try again. The engine is also checked automatically at every launch and " +
            "once a day while the app stays open.";

        private static readonly TimeSpan UpdateTimeout = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(20);

        private readonly string _ytdlpPath;
        private readonly string _engineDir;
        private readonly Func<string> _channel;
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <param name="channel">Returns the already-normalized release channel ("stable", "nightly" or "master").</param>
        public EngineUpdater(string ytdlpPath, string engineDir, Func<string> channel)
        {
            _ytdlpPath = ytdlpPath;
            _engineDir = engineDir;
            _channel = channel;
        }

        public async Task<EngineUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
        {
            if (!_gate.Wait(0))
                return new EngineUpdateResult(EngineUpdateStatus.AlreadyRunning, null, null, "An engine update is already running.");

            try
            {
                string? before = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
                var run = await RunAsync($"--update-to {_channel()}", UpdateTimeout, cancellationToken).ConfigureAwait(false);

                string? after = before;
                if (!run.TimedOut && run.ExitCode == 0)
                    after = await GetVersionAsync(cancellationToken).ConfigureAwait(false) ?? before;

                var result = Interpret(run.ExitCode, run.TimedOut, run.Text, before, after);
                AppLogger.Log($"[DEPENDENCIES] Engine update: {result.Status} (before {before ?? "?"}, after {after ?? "?"}).");
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[DEPENDENCIES ERROR] Execution of the update sub-process failed: {ex.Message}");
                return new EngineUpdateResult(EngineUpdateStatus.Failed, null, null, "The engine could not be started to check for updates.");
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Turns the raw output of "yt-dlp --update-to" into a result. Pure, so it is unit-testable.</summary>
        public static EngineUpdateResult Interpret(int exitCode, bool timedOut, string output, string? before, string? after)
        {
            if (timedOut)
                return new EngineUpdateResult(EngineUpdateStatus.Failed, before, after, "The update did not finish in time. Check the internet connection and try again.");

            bool saidUpdated = output.Contains("Updated yt-dlp", StringComparison.OrdinalIgnoreCase);

            if (!saidUpdated && (exitCode != 0 || output.Contains("ERROR:", StringComparison.OrdinalIgnoreCase)))
                return new EngineUpdateResult(EngineUpdateStatus.Failed, before, after, DescribeFailure(exitCode, output));

            bool versionChanged = before != null && after != null && !string.Equals(before, after, StringComparison.Ordinal);
            if (saidUpdated || versionChanged)
                return new EngineUpdateResult(EngineUpdateStatus.Updated, before, after, after != null ? $"The engine was updated to {after}." : "The engine was updated.");

            return new EngineUpdateResult(EngineUpdateStatus.UpToDate, before, after, "The engine is already up to date.");
        }

        private static string DescribeFailure(int exitCode, string output)
        {
            string[] networkSigns = { "Unable to obtain version info", "urlopen error", "getaddrinfo", "timed out", "Connection", "WinError 100" };
            if (networkSigns.Any(sign => output.Contains(sign, StringComparison.OrdinalIgnoreCase)))
                return "The update server could not be reached. Check the internet connection and try again.";

            string? errorLine = output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase));
            if (errorLine != null)
                return "The update failed: " + (errorLine.Length > 200 ? errorLine[..200] : errorLine);

            return $"The update failed (exit code {exitCode}).";
        }

        private async Task<string?> GetVersionAsync(CancellationToken cancellationToken)
        {
            try
            {
                var run = await RunAsync("--version", VersionTimeout, cancellationToken).ConfigureAwait(false);
                if (run.TimedOut || run.ExitCode != 0) return null;
                return run.Text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private readonly record struct ProcessOutput(int ExitCode, string Text, bool TimedOut);

        private async Task<ProcessOutput> RunAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ytdlpPath,
                Arguments = arguments,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _engineDir
            };

            psi.Environment.Remove("WT_SESSION");
            psi.Environment.Remove("WT_PROFILE_ID");

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("The engine process could not be started.");
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                await proc.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                if (cancellationToken.IsCancellationRequested) throw;
                AppLogger.Log("[DEPENDENCIES ERROR] Update process hung and was terminated.");
                return new ProcessOutput(-1, "", true);
            }

            return new ProcessOutput(proc.ExitCode, await stdout.ConfigureAwait(false) + Environment.NewLine + await stderr.ConfigureAwait(false), false);
        }
    }
}

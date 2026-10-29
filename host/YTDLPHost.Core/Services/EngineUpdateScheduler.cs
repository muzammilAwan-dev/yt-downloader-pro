namespace YTDLPHost.Services
{
    /// <summary>
    /// Lets an engine update run only when nothing is downloading. Implemented by <see cref="DownloadQueue"/>:
    /// while held, the queue starts no new download, so the engine file can be replaced safely.
    /// </summary>
    public interface IEngineUpdateGate
    {
        /// <summary>Stops new downloads from starting and returns true if none is running right now; otherwise changes nothing and returns false.</summary>
        bool TryHoldIfIdle();

        /// <summary>Lets the queue start downloads again (and wakes it up).</summary>
        void Release();
    }

    /// <summary>
    /// When the engine (yt-dlp) is updated: once at launch (beside the queue, as it always was), then again every
    /// <see cref="EngineUpdater.CheckInterval"/> while the app stays open (it often sits in the tray for days), and on demand through
    /// <see cref="RunIfIdleAsync"/>. A scheduled or manual update waits for running downloads instead of replacing the engine under them.
    /// </summary>
    public sealed class EngineUpdateScheduler : IDisposable
    {
        private readonly IEngineUpdateGate _gate;
        private readonly Func<CancellationToken, Task<EngineUpdateResult>> _update;
        private readonly TimeSpan _interval;
        private readonly TimeSpan _busyRetry;
        private readonly CancellationTokenSource _cts = new(); // never disposed on purpose: the loop may still read its token after Dispose()
        private int _started;

        /// <param name="update">Runs one update (normally <see cref="EngineUpdater.UpdateAsync"/>).</param>
        /// <param name="interval">Time between scheduled checks (default 24 h). Tests pass milliseconds.</param>
        /// <param name="busyRetry">Time before trying again when downloads were running (default 15 min).</param>
        public EngineUpdateScheduler(IEngineUpdateGate gate, Func<CancellationToken, Task<EngineUpdateResult>> update,
            TimeSpan? interval = null, TimeSpan? busyRetry = null)
        {
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _update = update ?? throw new ArgumentNullException(nameof(update));
            _interval = interval ?? EngineUpdater.CheckInterval;
            _busyRetry = busyRetry ?? EngineUpdater.BusyRetryInterval;
        }

        /// <summary>Starts the launch check and the daily loop. Calling it twice does nothing.</summary>
        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;
            _ = Task.Run(() => RunAsync(_cts.Token));
        }

        /// <summary>
        /// Runs one update now if no download is running. Returns null (nothing done) when the queue is busy or the first-time
        /// setup has not finished. The queue is held while the update runs and always released afterwards.
        /// </summary>
        public async Task<EngineUpdateResult?> RunIfIdleAsync(CancellationToken cancellationToken = default)
        {
            if (!_gate.TryHoldIfIdle()) return null;
            try
            {
                return await _update(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                // Launch check: not held, so queued downloads start without waiting for it (same behavior as before).
                await GuardedAsync(() => _update(token)).ConfigureAwait(false);

                var delay = _interval;
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                    var result = await GuardedAsync(() => RunIfIdleAsync(token)).ConfigureAwait(false);
                    delay = result == null ? _busyRetry : _interval;
                }
            }
            catch (OperationCanceledException)
            {
                // Dispose() or app exit.
            }
        }

        /// <summary>A failing update must never end the daily loop; cancellation still does.</summary>
        private static async Task<T?> GuardedAsync<T>(Func<Task<T>> action) where T : class
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[DEPENDENCIES ERROR] Scheduled engine update failed: {ex.Message}");
                return null;
            }
        }

        public void Dispose() => _cts.Cancel();
    }
}

using YTDLPHost.Models;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Saves the download list to history.json shortly after it changes. Changes that arrive close together are
    /// collapsed into one write of the latest state (the old view-model code did the same with a 2.5 s delay).
    /// New in 1.5e: <see cref="Dispose"/> writes any save that is still waiting, so changes made in the last
    /// moments before the app exits are no longer lost.
    /// </summary>
    public sealed class HistorySaver : IDisposable
    {
        private readonly IUiDispatcher _ui;
        private readonly Func<IReadOnlyList<DownloadTask>> _snapshot;
        private readonly Action<IReadOnlyList<DownloadTask>> _save;
        private readonly TimeSpan _delay;
        private readonly object _gate = new();

        private CancellationTokenSource? _cts;
        private IReadOnlyList<DownloadTask>? _pending;
        private bool _disposed;

        /// <param name="snapshot">Copies the current tasks. Called through <paramref name="ui"/>, because the list belongs to the UI thread.</param>
        /// <param name="save">Writes a snapshot. Defaults to <see cref="HistoryManager.SaveHistory"/>.</param>
        /// <param name="delay">Quiet time before writing (default 2.5 s).</param>
        public HistorySaver(
            IUiDispatcher ui,
            Func<IReadOnlyList<DownloadTask>> snapshot,
            Action<IReadOnlyList<DownloadTask>>? save = null,
            TimeSpan? delay = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _save = save ?? (tasks => HistoryManager.SaveHistory(tasks));
            _delay = delay ?? TimeSpan.FromMilliseconds(2500);
        }

        /// <summary>Takes a snapshot now and writes it after the quiet time, unless another request replaces it first.</summary>
        public void RequestSave()
        {
            IReadOnlyList<DownloadTask> snapshot = Array.Empty<DownloadTask>();
            _ui.Invoke(() => snapshot = _snapshot());

            CancellationToken token;
            lock (_gate)
            {
                if (_disposed) return;
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                token = _cts.Token;
                _pending = snapshot;
            }

            _ = SaveAfterDelayAsync(snapshot, token);
        }

        private async Task SaveAfterDelayAsync(IReadOnlyList<DownloadTask> snapshot, CancellationToken token)
        {
            try
            {
                await Task.Delay(_delay, token).ConfigureAwait(false);

                lock (_gate)
                {
                    if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_pending, snapshot)) return;
                    _pending = null;
                }

                _save(snapshot);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLogger.Log($"[HISTORY] Background save failed: {ex.Message}");
            }
        }

        /// <summary>Stops the timer and writes a save that is still waiting. Safe to call more than once.</summary>
        public void Dispose()
        {
            IReadOnlyList<DownloadTask>? pending;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _cts?.Cancel();
                pending = _pending;
                _pending = null;
            }

            if (pending == null) return;
            try { _save(pending); }
            catch (Exception ex) { AppLogger.Log($"[HISTORY] Final save failed: {ex.Message}"); }
        }
    }
}

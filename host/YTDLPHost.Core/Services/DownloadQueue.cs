using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using YTDLPHost.Models;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Decides which queued download runs next and owns the running <see cref="IDownloadRunner"/>s.
    /// Extracted from MainViewModel (Phase 1.5c). Behavior kept: at most N downloads at once, and two downloads
    /// of the same video never run together (the second shows "Waiting (Similar video active)..." until the first ends).
    ///
    /// Threading rules (read before changing anything):
    ///  * The item list belongs to the UI thread. The queue only reads or changes items inside IUiDispatcher
    ///    calls, never directly, so it is safe to run the scheduling loop on a thread-pool thread.
    ///  * Every event below is raised on the UI thread, and a handler that throws is logged, not propagated,
    ///    so a bad subscriber can never leave an item marked "Downloading" without a runner.
    ///  * Awaits here use ConfigureAwait(false) on purpose: nothing after an await needs the UI context
    ///    because all UI work goes through the dispatcher.
    /// </summary>
    public sealed class DownloadQueue : IDisposable
    {
        public const string PhaseQueued = "Queued";
        public const string PhaseStarting = "Starting...";
        public const string PhaseWaitingSimilar = "Waiting (Similar video active)...";
        public const string PhaseApplyingSettings = "Applying Settings...";

        private readonly ObservableCollection<DownloadItemViewModel> _items;
        private readonly IUiDispatcher _ui;
        private readonly Func<IDownloadRunner> _runnerFactory;
        private readonly Func<int> _maxConcurrent;
        private readonly TimeSpan _startSpacing;
        private readonly ConcurrentDictionary<Guid, IDownloadRunner> _active = new();
        private readonly CancellationTokenSource _cts = new(); // never disposed on purpose: the loop may still read its token after Dispose()

        // 0/1 flags, only touched through Interlocked/Volatile.
        private int _running;     // a scheduling loop is active
        private int _dirty;       // something changed since the loop last looked
        private int _engineReady; // yt-dlp/ffmpeg are installed, so downloads may start
        private int _disposed;

        /// <summary>An item was just switched to Downloading (UI thread, inside the selection step).</summary>
        public event Action<DownloadItemViewModel>? ItemStarted;

        /// <summary>Progress, info, error, or a runner ended: counts and displays may need a refresh (UI thread).</summary>
        public event Action<DownloadItemViewModel>? ItemChanged;

        /// <summary>A download finished successfully; the string is the title reported by the runner (UI thread).</summary>
        public event Action<DownloadItemViewModel, string>? ItemCompleted;

        /// <summary>The queue has nothing running and nothing waiting (UI thread).</summary>
        public event Action? Drained;

        /// <param name="items">The UI-bound list. Owned by the caller; touched only through <paramref name="ui"/>.</param>
        /// <param name="runnerFactory">Creates one runner per download.</param>
        /// <param name="maxConcurrent">Read each time the queue looks for work, so settings changes apply at once. Values below 1 count as 1.</param>
        /// <param name="startSpacing">Pause between two starts (default 1 s, as before). Tests pass zero.</param>
        public DownloadQueue(
            ObservableCollection<DownloadItemViewModel> items,
            IUiDispatcher ui,
            Func<IDownloadRunner> runnerFactory,
            Func<int> maxConcurrent,
            TimeSpan? startSpacing = null)
        {
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _runnerFactory = runnerFactory ?? throw new ArgumentNullException(nameof(runnerFactory));
            _maxConcurrent = maxConcurrent ?? throw new ArgumentNullException(nameof(maxConcurrent));
            _startSpacing = startSpacing ?? TimeSpan.FromSeconds(1);
        }

        /// <summary>Number of registered runners (the slots in use).</summary>
        public int ActiveCount => _active.Count;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        /// <summary>Allows downloads to start. Call <see cref="Trigger"/> afterwards (or wait for the next one).</summary>
        public void MarkEngineReady() => Interlocked.Exchange(ref _engineReady, 1);

        /// <summary>Asks the queue to look for work. Cheap, thread-safe, and safe to call as often as you like.</summary>
        public void Trigger()
        {
            if (IsDisposed) return;
            Interlocked.Exchange(ref _dirty, 1);
            StartLoopIfIdle();
        }

        private void StartLoopIfIdle()
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return; // the active loop will see _dirty
            _ = Task.Run(RunLoopAsync);
        }

        private async Task RunLoopAsync()
        {
            try
            {
                while (!IsDisposed)
                {
                    Interlocked.Exchange(ref _dirty, 0);
                    if (Volatile.Read(ref _engineReady) == 0) break;

                    int max = ReadMaxConcurrent();
                    bool startedAny = false;

                    while (_active.Count < max && !IsDisposed)
                    {
                        var next = SelectAndMarkNext();
                        if (next == null) break;

                        StartItem(next);
                        startedAny = true;

                        await Task.Delay(_startSpacing, _cts.Token).ConfigureAwait(false);
                    }

                    if (!startedAny)
                    {
                        if (Volatile.Read(ref _dirty) == 1) continue; // something changed while we were looking
                        break;
                    }
                }

                RaiseDrainedIfIdle();
            }
            catch (OperationCanceledException)
            {
                // Dispose() cancelled the start spacing.
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[QUEUE ERROR] Scheduling loop fault: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
                // A Trigger() that arrived after our last look but before the line above saw _running == 1 and left the work to us.
                if (Volatile.Read(ref _dirty) == 1 && !IsDisposed) StartLoopIfIdle();
            }
        }

        private int ReadMaxConcurrent()
        {
            try { return Math.Max(1, _maxConcurrent()); }
            catch (Exception ex)
            {
                AppLogger.Log($"[QUEUE ERROR] Could not read the concurrency limit, using 1: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// On the UI thread: updates the "Waiting (Similar video active)..." markers, then picks the first queued item whose
        /// video is not already downloading (and that has no runner still winding down), and switches it to Downloading.
        /// </summary>
        private DownloadItemViewModel? SelectAndMarkNext()
        {
            DownloadItemViewModel? next = null;

            _ui.Invoke(() =>
            {
                var activeVideoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in _items)
                {
                    if (d.Task.Status == DownloadStatus.Downloading)
                        activeVideoIds.Add(CommandInfo.ExtractVideoId(d.Task.Command));
                }

                foreach (var d in _items)
                {
                    if (d.Task.Status != DownloadStatus.Queued) continue;

                    string videoId = CommandInfo.ExtractVideoId(d.Task.Command);
                    if (activeVideoIds.Contains(videoId))
                    {
                        if (d.Task.CurrentPhase != PhaseWaitingSimilar)
                        {
                            d.Task.CurrentPhase = PhaseWaitingSimilar;
                            d.Refresh();
                        }
                    }
                    else if (d.Task.CurrentPhase == PhaseWaitingSimilar)
                    {
                        d.Task.CurrentPhase = PhaseQueued;
                        d.Refresh();
                    }
                }

                next = _items.FirstOrDefault(d =>
                    d.Task.Status == DownloadStatus.Queued &&
                    !_active.ContainsKey(d.Id) &&
                    !activeVideoIds.Contains(CommandInfo.ExtractVideoId(d.Task.Command)));

                if (next == null) return;

                next.Task.Status = DownloadStatus.Downloading;
                next.Task.CurrentPhase = PhaseStarting;
                next.Refresh();
                Raise(ItemStarted, next);
            });

            return next;
        }

        private void StartItem(DownloadItemViewModel vm)
        {
            AppLogger.Log($"[QUEUE] Spawning execution runner for task ID: {vm.Id}");

            var runner = _runnerFactory();
            if (!_active.TryAdd(vm.Id, runner))
            {
                // Cannot normally happen (selection skips items that still have a runner); never leave the item stuck if it does.
                AppLogger.Log($"[QUEUE ERROR] A runner is already registered for task ID: {vm.Id}. Returning it to the queue.");
                try { runner.Dispose(); } catch { }
                _ui.Invoke(() =>
                {
                    if (vm.Task.Status == DownloadStatus.Downloading)
                    {
                        vm.Task.Status = DownloadStatus.Queued;
                        vm.Task.CurrentPhase = PhaseQueued;
                        vm.Refresh();
                    }
                });
                return;
            }

            // The runner is bound to this one item, so the handlers capture it directly (no list lookup from a worker thread).
            runner.OnProgressUpdate += (_, _) => PostChanged(vm);
            runner.OnDownloadError += (_, _) => PostChanged(vm);
            runner.OnInfoExtracted += (_, _) => PostChanged(vm);
            runner.OnDownloadComplete += (_, e) => PostCompleted(vm, e.Title);

            _ = RunItemAsync(vm, runner);
        }

        private async Task RunItemAsync(DownloadItemViewModel vm, IDownloadRunner runner)
        {
            try
            {
                await runner.ExecuteAsync(vm.Task).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The real runner catches its own errors; this keeps a bug (or a fake) from leaking the slot and the item.
                AppLogger.Log($"[QUEUE ERROR] Runner fault for task ID {vm.Id}: {ex.Message}");
                _ui.Invoke(() =>
                {
                    if (vm.Task.Status == DownloadStatus.Downloading)
                    {
                        vm.Task.Status = DownloadStatus.Error;
                        vm.Task.IsIndeterminate = false;
                        vm.Task.ErrorMessage = $"Unexpected error: {ex.Message}";
                        vm.Refresh();
                    }
                });
            }
            finally
            {
                // Remove only OUR registration. If the item was paused and resumed meanwhile, a newer runner now owns the slot.
                _active.TryRemove(new KeyValuePair<Guid, IDownloadRunner>(vm.Id, runner));
                try { runner.Dispose(); }
                catch (Exception ex) { AppLogger.Log($"[QUEUE ERROR] Runner dispose fault: {ex.Message}"); }

                _ui.Post(() => Raise(ItemChanged, vm));
                Trigger();
            }
        }

        private void PostChanged(DownloadItemViewModel vm) =>
            _ui.Post(UiPriority.Background, () =>
            {
                if (!_items.Contains(vm)) return; // removed from the list meanwhile
                vm.Refresh();
                Raise(ItemChanged, vm);
            });

        private void PostCompleted(DownloadItemViewModel vm, string title) =>
            _ui.Post(UiPriority.Background, () =>
            {
                if (!_items.Contains(vm)) return;
                vm.Refresh();
                try { ItemCompleted?.Invoke(vm, title); }
                catch (Exception ex) { AppLogger.Log($"[QUEUE ERROR] Event handler fault: {ex.Message}"); }
            });

        private void RaiseDrainedIfIdle() =>
            _ui.Invoke(() =>
            {
                if (_active.IsEmpty && !_items.Any(d => d.Task.Status == DownloadStatus.Queued))
                    Raise(Drained);
            });

        /// <summary>
        /// Stops the runner of an item (pause / cancel / remove) and frees its slot at once. Returns false if it had none.
        /// The kill itself can take seconds (the runner waits for the process tree to exit), so it runs off the calling
        /// thread and the caller awaits it instead of freezing the UI. The caller sets the item's new status afterwards.
        /// </summary>
        public async Task<bool> StopAsync(Guid itemId)
        {
            if (!_active.TryRemove(itemId, out var runner)) return false;
            await Task.Run(() => runner.Cancel()).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Stops a running item and puts it back in the queue so it restarts with the current settings
        /// (used when the speed limit or concurrency limit changes). Does not call <see cref="Trigger"/>.
        /// </summary>
        public async Task SuspendToQueueAsync(DownloadItemViewModel vm)
        {
            AppLogger.Log($"[QUEUE] Hot-swapping task ID: {vm.Id} to apply dynamic settings.");

            _ui.Invoke(() =>
            {
                vm.Task.CurrentPhase = PhaseApplyingSettings;
                vm.Refresh();
            });

            if (_active.TryRemove(vm.Id, out var runner))
            {
                await Task.Run(() => runner.Cancel()).ConfigureAwait(false);
            }

            _ui.Invoke(() =>
            {
                var s = vm.Task.Status;
                if (s != DownloadStatus.Cancelled && s != DownloadStatus.Error && s != DownloadStatus.Completed)
                {
                    vm.Task.Status = DownloadStatus.Queued;
                    vm.Refresh();
                    Raise(ItemChanged, vm);
                }
            });
        }

        /// <summary>
        /// Applies saved settings to running downloads. A speed-limit change restarts every running download so the new limit
        /// takes effect; a lowered concurrency limit puts the excess (the last ones in the list) back in the queue. Only items
        /// that really have a runner are touched (never the setup card). Calls <see cref="Trigger"/> at the end.
        /// </summary>
        public async Task ApplySettingsAsync(bool speedChanged, bool maxChanged, int newMaxConcurrent)
        {
            var toSuspend = new List<DownloadItemViewModel>();

            _ui.Invoke(() =>
            {
                var running = _items
                    .Where(d => d.Task.Status == DownloadStatus.Downloading && _active.ContainsKey(d.Id))
                    .ToList();

                if (speedChanged)
                {
                    AppLogger.Log("[SETTINGS] Speed limit modified. Suspending active tasks to apply new throttle rules...");
                    toSuspend = running;
                }
                else if (maxChanged && _active.Count > newMaxConcurrent)
                {
                    AppLogger.Log("[SETTINGS] Max concurrent slots reduced. Suspending excess tasks...");
                    int excess = _active.Count - newMaxConcurrent;
                    running.Reverse();
                    toSuspend = running.Take(excess).ToList();
                }
            });

            foreach (var vm in toSuspend)
            {
                await SuspendToQueueAsync(vm).ConfigureAwait(false);
            }

            Trigger();
        }

        /// <summary>
        /// Asks every running download to stop (application exit) and waits for all of them together, at most 10 s,
        /// so N slow kills cost the slowest one and not their sum. Registrations stay until each runner ends.
        /// </summary>
        public void CancelAll()
        {
            var cancels = _active.Values.Select(runner => Task.Run(() =>
            {
                try { runner.Cancel(); }
                catch (Exception ex) { AppLogger.Log($"[QUEUE ERROR] Runner cancel fault: {ex.Message}"); }
            })).ToArray();

            try { Task.WaitAll(cancels, TimeSpan.FromSeconds(10)); }
            catch (Exception ex) { AppLogger.Log($"[QUEUE ERROR] Waiting for runners to cancel failed: {ex.Message}"); }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _cts.Cancel(); } catch { }

            foreach (var runner in _active.Values)
            {
                try { runner.Dispose(); } catch { }
            }
        }

        private static void Raise<T>(Action<T>? handler, T arg)
        {
            try { handler?.Invoke(arg); }
            catch (Exception ex) { AppLogger.Log($"[QUEUE ERROR] Event handler fault: {ex.Message}"); }
        }

        private static void Raise(Action? handler)
        {
            try { handler?.Invoke(); }
            catch (Exception ex) { AppLogger.Log($"[QUEUE ERROR] Event handler fault: {ex.Message}"); }
        }
    }
}

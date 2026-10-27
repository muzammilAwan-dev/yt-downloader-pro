using System.Globalization;
using YTDLPHost.Models;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Native
{
    /// <summary>
    /// One shim connection (Phase 2, step 2.3): reads length-prefixed requests, answers them, and keeps telling the browser how each
    /// accepted download is doing until it ends. Portable (a plain <see cref="Stream"/>), so tests drive it over a real pipe.
    ///
    /// Rules:
    ///  * messages are parsed by <see cref="NativeRequestParser"/> and built into arguments by <see cref="DownloadRequestMapper"/>; nothing from
    ///    the browser is trusted before that, and the save folder comes from the host settings, never from the request;
    ///  * one request can make several downloads (one per URL). The browser still gets ONE accepted and ONE final done/error for its id,
    ///    with the progress averaged over the downloads;
    ///  * downloads belong to the app: when the connection closes, the watchers stop but the downloads carry on;
    ///  * all writes go through one gate, with a timeout, so a stuck reader cannot freeze the app and frames never interleave.
    /// </summary>
    public sealed class NativeConnection
    {
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);

        private readonly Stream _stream;
        private readonly INativeDownloadSink _sink;
        private readonly Func<MapperSettings> _settings;
        private readonly string _hostVersion;
        private readonly TimeSpan _pollInterval;
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        public NativeConnection(Stream stream, INativeDownloadSink sink, Func<MapperSettings> settings, string hostVersion, TimeSpan? pollInterval = null)
        {
            _stream = stream;
            _sink = sink;
            _settings = settings;
            _hostVersion = hostVersion;
            _pollInterval = pollInterval ?? DefaultPollInterval;
        }

        /// <summary>Serves the connection until the other side closes it, the framing breaks, or <paramref name="cancellationToken"/> fires. Never throws for those cases.</summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using var watchers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var running = new List<Task>();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    byte[]? message;
                    try
                    {
                        message = await NativeFrame.ReadAsync(_stream, NativeFrame.MaxBrowserToHostBytes, cancellationToken).ConfigureAwait(false);
                    }
                    catch (NativeProtocolException ex)
                    {
                        // The framing is broken, so nothing that follows can be trusted: drop the connection.
                        AppLogger.Log($"[NATIVE] Closing a connection after a framing error: {ex.Message}");
                        break;
                    }

                    if (message == null) break; // the shim closed its end

                    try
                    {
                        await HandleAsync(message, watchers.Token, running).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not IOException && ex is not ObjectDisposedException)
                    {
                        AppLogger.Log($"[NATIVE] A request failed unexpectedly: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                watchers.Cancel();
                try { await Task.WhenAll(running).ConfigureAwait(false); } catch { /* the watchers swallow their own errors */ }
            }
        }

        // ------------------------------------------------------------------ one message

        private async Task HandleAsync(byte[] message, CancellationToken cancellationToken, List<Task> running)
        {
            var parsed = NativeRequestParser.Parse(message);
            if (!parsed.Ok)
            {
                await SendAsync(NativeResponses.Rejected(parsed.Id, parsed.Error!), cancellationToken).ConfigureAwait(false);
                return;
            }

            switch (parsed.Request)
            {
                case HelloRequest hello:
                    await SendAsync(NativeResponses.Hello(hello.Id, _hostVersion), cancellationToken).ConfigureAwait(false);
                    break;
                case DownloadRequest download:
                    await HandleDownloadAsync(download, cancellationToken, running).ConfigureAwait(false);
                    break;
            }
        }

        private async Task HandleDownloadAsync(DownloadRequest request, CancellationToken cancellationToken, List<Task> running)
        {
            var mapped = DownloadRequestMapper.Map(request, _settings());
            if (!mapped.Ok)
            {
                await SendAsync(NativeResponses.Rejected(request.Id, new NativeParseError(NativeErrorCode.BadField, mapped.Error!)), cancellationToken).ConfigureAwait(false);
                return;
            }

            NativeSubmission submission;
            try
            {
                submission = _sink.Submit(mapped.Downloads);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[NATIVE] The app could not queue a request: {ex.Message}");
                await SendAsync(NativeResponses.Error(request.Id, "The app could not start the download."), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (submission.Items.Count == 0)
            {
                await SendAsync(NativeResponses.Error(request.Id, submission.Error ?? "The app could not start the download."), cancellationToken).ConfigureAwait(false);
                return;
            }

            await SendAsync(NativeResponses.Accepted(request.Id), cancellationToken).ConfigureAwait(false);
            running.Add(Task.Run(() => WatchAsync(request.Id, submission.Items, cancellationToken), CancellationToken.None));
        }

        // ------------------------------------------------------------------ progress

        private enum Outcome { Active, Done, Failed }

        private readonly record struct JobState(Outcome Outcome, string Text);

        private async Task WatchAsync(string id, IReadOnlyList<DownloadItemViewModel> items, CancellationToken cancellationToken)
        {
            string? lastKey = null;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var listed = _sink.ListedIds();
                    var states = items.Select(item => Evaluate(item, listed.Contains(item.Id))).ToList();

                    if (states.All(s => s.Outcome != Outcome.Active))
                    {
                        await SendAsync(FinalMessage(id, states), cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    var (key, bytes) = ProgressMessage(id, items, states);
                    if (key != lastKey)
                    {
                        lastKey = key;
                        await SendAsync(bytes, cancellationToken).ConfigureAwait(false);
                    }

                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                AppLogger.Log($"[NATIVE] Progress reporting stopped: {ex.Message}");
            }
        }

        private static JobState Evaluate(DownloadItemViewModel item, bool listed)
        {
            var task = item.Task;
            switch (task.Status)
            {
                case DownloadStatus.Completed:
                    return new JobState(Outcome.Done, task.ResultNote ?? "");
                case DownloadStatus.Error:
                    return new JobState(Outcome.Failed, string.IsNullOrWhiteSpace(task.ErrorMessage) ? "The download failed." : task.ErrorMessage);
                case DownloadStatus.Cancelled:
                    return new JobState(Outcome.Failed, "The download was cancelled in the app.");
            }

            // Queued, downloading or paused: still alive, unless the user took it out of the list.
            return listed ? new JobState(Outcome.Active, "") : new JobState(Outcome.Failed, "The download was removed in the app.");
        }

        private static byte[] FinalMessage(string id, List<JobState> states)
        {
            int failed = states.Count(s => s.Outcome == Outcome.Failed);
            int done = states.Count - failed;

            if (failed == 0)
            {
                string? note = states.Count == 1 && !string.IsNullOrWhiteSpace(states[0].Text) ? states[0].Text : null;
                return NativeResponses.Done(id, note);
            }

            string firstFailure = states.First(s => s.Outcome == Outcome.Failed).Text;
            if (done == 0)
                return NativeResponses.Error(id, states.Count == 1 ? firstFailure : $"{failed} of {states.Count} downloads failed: {firstFailure}");

            return NativeResponses.Done(id, $"{done} finished, {failed} failed: {firstFailure}");
        }

        private static (string Key, byte[] Bytes) ProgressMessage(string id, IReadOnlyList<DownloadItemViewModel> items, List<JobState> states)
        {
            double percent = 0;
            for (int i = 0; i < items.Count; i++)
                percent += states[i].Outcome == Outcome.Done ? 100.0 : Math.Clamp(items[i].Task.Progress, 0.0, 100.0);
            percent /= items.Count;

            var active = items.FirstOrDefault(item => item.Task.Status == DownloadStatus.Downloading);
            string speed = active?.Task.Speed ?? "";
            string eta = active?.Task.Eta ?? "";
            string title = (active ?? items[0]).DisplayTitle;

            string phase;
            if (items.Count == 1)
                phase = items[0].Task.Status == DownloadStatus.Paused ? "Paused" : items[0].Task.CurrentPhase ?? "";
            else
                phase = $"{states.Count(s => s.Outcome != Outcome.Active)}/{items.Count} finished";

            string key = string.Join("|", percent.ToString("F1", CultureInfo.InvariantCulture), speed, eta, phase, title);
            return (key, NativeResponses.Progress(id, percent, speed, eta, phase, title));
        }

        // ------------------------------------------------------------------ writing

        private async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(WriteTimeout);
                await NativeFrame.WriteAsync(_stream, payload, NativeFrame.MaxHostToBrowserBytes, timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }
    }
}

using YTDLPHost.Native;

namespace YTDLPHost.Shim
{
    /// <summary>Why the bridge stopped.</summary>
    public enum ShimEnd
    {
        /// <summary>The browser closed its end (normal: the extension disconnected or the browser exited).</summary>
        BrowserClosed,

        /// <summary>The app closed its end (it exited or was stopped).</summary>
        AppClosed,

        /// <summary>A frame broke the framing rules or a stream failed; see the message.</summary>
        Error
    }

    /// <summary>
    /// Who may start the shim. The browser passes the caller's origin as the FIRST argument ("chrome-extension://ID/"); on Windows it
    /// may add "--parent-window=..." after it. The host manifest's allowed_origins already makes Chrome refuse other extensions; this is
    /// the second lock, so a stray program that runs the shim directly cannot talk to the app through it.
    /// </summary>
    public static class ShimOrigin
    {
        /// <summary>One origin per line; blank lines and lines starting with # are ignored.</summary>
        public static List<string> ParseAllowed(string text) =>
            text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToList();

        /// <summary>True only if the first argument is exactly one of the allowed origins (case-sensitive, trailing slash included).</summary>
        public static bool IsAllowed(string[] args, IReadOnlyCollection<string> allowed) =>
            args.Length > 0 && allowed.Contains(args[0], StringComparer.Ordinal);
    }

    /// <summary>
    /// Copies whole frames both ways until one side ends. It never looks inside a frame (the app parses and validates everything), it
    /// only enforces the size limits so a broken or hostile peer cannot make it allocate without bound.
    /// </summary>
    public static class ShimBridge
    {
        public sealed record Outcome(ShimEnd End, string? Detail);

        /// <param name="fromBrowser">The browser's stdin stream.</param>
        /// <param name="toBrowser">The browser's stdout stream: frames only, never text.</param>
        /// <param name="app">The duplex pipe to the app.</param>
        public static async Task<Outcome> RunAsync(Stream fromBrowser, Stream toBrowser, Stream app, CancellationToken cancellationToken)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var up = PumpAsync(fromBrowser, app, NativeFrame.MaxBrowserToHostBytes, ShimEnd.BrowserClosed, stop.Token);
            var down = PumpAsync(app, toBrowser, NativeFrame.MaxHostToBrowserBytes, ShimEnd.AppClosed, stop.Token);

            var first = await Task.WhenAny(up, down).ConfigureAwait(false);
            var outcome = await first.ConfigureAwait(false);
            stop.Cancel(); // the other pump may be stuck in a read; the caller exits the process right after this
            return outcome;
        }

        private static async Task<Outcome> PumpAsync(Stream source, Stream destination, int maxBytes, ShimEnd endedWhenSourceCloses, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var frame = await NativeFrame.ReadAsync(source, maxBytes, cancellationToken).ConfigureAwait(false);
                    if (frame == null) return new Outcome(endedWhenSourceCloses, null);
                    await NativeFrame.WriteAsync(destination, frame, maxBytes, cancellationToken).ConfigureAwait(false);
                }
                return new Outcome(endedWhenSourceCloses, "cancelled");
            }
            catch (OperationCanceledException)
            {
                return new Outcome(endedWhenSourceCloses, "cancelled");
            }
            catch (NativeProtocolException ex)
            {
                return new Outcome(ShimEnd.Error, ex.Message);
            }
            catch (IOException ex)
            {
                // A pipe closing in the middle of a read or write is how the peer going away often looks.
                return new Outcome(ShimEnd.Error, ex.Message);
            }
            catch (ObjectDisposedException ex)
            {
                return new Outcome(ShimEnd.Error, ex.Message);
            }
        }
    }
}

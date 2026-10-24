namespace YTDLPHost.Native
{
    /// <summary>The stream broke the Native Messaging framing rules (truncated, oversized, or too large to send).</summary>
    public sealed class NativeProtocolException : Exception
    {
        public NativeProtocolException(string message) : base(message) { }
    }

    /// <summary>
    /// The Chrome Native Messaging wire format (verified against the Chrome docs): every message is UTF-8 JSON preceded by a
    /// 32-bit length in the machine's NATIVE byte order. Chrome accepts at most 1 MB per message FROM the host and allows up to
    /// 4 GB TO the host. Pure stream code with no process or console access, so it is testable; the shim and the app use it on
    /// the raw stdin/stdout streams (never Console.Out: on Windows its text mode would corrupt the bytes).
    /// </summary>
    public static class NativeFrame
    {
        /// <summary>Chrome's hard limit for a message sent by the host to the browser.</summary>
        public const int MaxHostToBrowserBytes = 1024 * 1024;

        /// <summary>Our own, much lower, cap for messages from the browser (Chrome allows 4 GB). It bounds memory use; cookies and a few hundred URLs fit easily.</summary>
        public const int MaxBrowserToHostBytes = 8 * 1024 * 1024;

        /// <summary>Reads one message. Returns null if the stream ended cleanly between messages.</summary>
        /// <exception cref="NativeProtocolException">Truncated header or body, or a length above <paramref name="maxBytes"/> (checked before any allocation).</exception>
        public static async Task<byte[]?> ReadAsync(Stream input, int maxBytes = MaxBrowserToHostBytes, CancellationToken cancellationToken = default)
        {
            var header = new byte[4];
            int headerBytes = await ReadFullyAsync(input, header, 4, cancellationToken).ConfigureAwait(false);
            if (headerBytes == 0) return null;
            if (headerBytes < 4) throw new NativeProtocolException("The stream ended inside a message length.");

            uint length = BitConverter.ToUInt32(header, 0); // native byte order, as the protocol requires
            if (length > (uint)maxBytes) throw new NativeProtocolException($"A message of {length} bytes is larger than the {maxBytes} byte limit.");

            var payload = new byte[length];
            int bodyBytes = await ReadFullyAsync(input, payload, (int)length, cancellationToken).ConfigureAwait(false);
            if (bodyBytes < length) throw new NativeProtocolException("The stream ended inside a message.");
            return payload;
        }

        /// <summary>Writes one message as a single write (so frames can never interleave) and flushes.</summary>
        /// <exception cref="NativeProtocolException">The payload is larger than <paramref name="maxBytes"/>.</exception>
        public static async Task WriteAsync(Stream output, byte[] payload, int maxBytes = MaxHostToBrowserBytes, CancellationToken cancellationToken = default)
        {
            if (payload.Length > maxBytes) throw new NativeProtocolException($"A message of {payload.Length} bytes is larger than the {maxBytes} byte limit.");

            var frame = new byte[4 + payload.Length];
            BitConverter.GetBytes((uint)payload.Length).CopyTo(frame, 0);
            payload.CopyTo(frame, 4);
            await output.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        // A stream may return fewer bytes than asked (pipes do, often one chunk at a time), so keep reading until done or the end.
        private static async Task<int> ReadFullyAsync(Stream input, byte[] buffer, int count, CancellationToken cancellationToken)
        {
            int total = 0;
            while (total < count)
            {
                int read = await input.ReadAsync(buffer.AsMemory(total, count - total), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
            }
            return total;
        }
    }
}

using Xunit;
using YTDLPHost.Native;

namespace YTDLPHost.Tests.Native;

public class NativeFrameTests
{
    /// <summary>A pipe-like stream that hands out at most N bytes per read, like a real stdin does.</summary>
    private sealed class TrickleStream : MemoryStream
    {
        private readonly int _chunk;
        public TrickleStream(byte[] data, int chunk) : base(data) { _chunk = chunk; }
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, _chunk));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer.Length > _chunk ? buffer[.._chunk] : buffer, cancellationToken);
    }

    private static byte[] Frame(byte[] payload)
    {
        var bytes = new byte[4 + payload.Length];
        BitConverter.GetBytes((uint)payload.Length).CopyTo(bytes, 0);
        payload.CopyTo(bytes, 4);
        return bytes;
    }

    [Fact]
    public async Task A_written_message_is_read_back_unchanged()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"v\":1,\"text\":\"h\u00E9llo \u4E16\u754C\"}");
        using var stream = new MemoryStream();

        await NativeFrame.WriteAsync(stream, payload);
        stream.Position = 0;
        var read = await NativeFrame.ReadAsync(stream);

        Assert.Equal(payload, read);
    }

    [Fact]
    public async Task The_length_prefix_is_four_bytes_in_native_byte_order_and_counts_bytes_not_characters()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("\u00E9\u00E9"); // 2 characters, 4 bytes
        using var stream = new MemoryStream();

        await NativeFrame.WriteAsync(stream, payload);

        var written = stream.ToArray();
        Assert.Equal(4 + 4, written.Length);
        Assert.Equal(4u, BitConverter.ToUInt32(written, 0));
    }

    [Fact]
    public async Task Several_messages_are_read_in_order_and_the_clean_end_returns_null()
    {
        using var stream = new MemoryStream();
        await NativeFrame.WriteAsync(stream, new byte[] { 1 });
        await NativeFrame.WriteAsync(stream, new byte[] { 2, 2 });
        stream.Position = 0;

        Assert.Equal(new byte[] { 1 }, await NativeFrame.ReadAsync(stream));
        Assert.Equal(new byte[] { 2, 2 }, await NativeFrame.ReadAsync(stream));
        Assert.Null(await NativeFrame.ReadAsync(stream));
    }

    [Fact]
    public async Task A_stream_that_returns_one_byte_at_a_time_still_yields_whole_messages()
    {
        var payload = new byte[300];
        new Random(1).NextBytes(payload);
        using var stream = new TrickleStream(Frame(payload), chunk: 1);

        var read = await NativeFrame.ReadAsync(stream);

        Assert.Equal(payload, read);
    }

    [Fact]
    public async Task An_empty_message_is_returned_as_an_empty_array()
    {
        using var stream = new MemoryStream(Frame(Array.Empty<byte>()));

        var read = await NativeFrame.ReadAsync(stream);

        Assert.NotNull(read);
        Assert.Empty(read!);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task A_stream_that_ends_inside_the_length_is_a_protocol_error(int headerBytes)
    {
        using var stream = new MemoryStream(new byte[headerBytes]);

        await Assert.ThrowsAsync<NativeProtocolException>(() => NativeFrame.ReadAsync(stream));
    }

    [Fact]
    public async Task A_stream_that_ends_inside_the_message_is_a_protocol_error()
    {
        var frame = Frame(new byte[10]);
        using var stream = new MemoryStream(frame, 0, frame.Length - 3);

        await Assert.ThrowsAsync<NativeProtocolException>(() => NativeFrame.ReadAsync(stream));
    }

    [Fact]
    public async Task A_message_over_the_limit_is_refused_before_any_memory_is_allocated_for_it()
    {
        // The header claims ~4 GB but no body follows: the limit check must fire first (otherwise this would try to allocate it).
        using var stream = new MemoryStream(BitConverter.GetBytes(uint.MaxValue));

        await Assert.ThrowsAsync<NativeProtocolException>(() => NativeFrame.ReadAsync(stream));
    }

    [Fact]
    public async Task Writing_more_than_chromes_one_megabyte_limit_is_refused()
    {
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<NativeProtocolException>(() => NativeFrame.WriteAsync(stream, new byte[NativeFrame.MaxHostToBrowserBytes + 1]));
        Assert.Equal(0, stream.Length); // nothing half-written
    }

    [Fact]
    public async Task Exactly_one_megabyte_is_allowed()
    {
        using var stream = new MemoryStream();

        await NativeFrame.WriteAsync(stream, new byte[NativeFrame.MaxHostToBrowserBytes]);

        Assert.Equal(4 + NativeFrame.MaxHostToBrowserBytes, stream.Length);
    }
}

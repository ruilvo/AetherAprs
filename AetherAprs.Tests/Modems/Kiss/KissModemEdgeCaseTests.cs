// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Modems.Kiss;
using Xunit;

namespace AetherAprs.Tests.Modems.Kiss;

public class KissModemEdgeCaseTests
{
    // ---------------------------------------------------------------
    // Send concurrency (send lock)
    // ---------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ConcurrentSends_FramesDoNotInterleave()
    {
        using var stream = new SlowWriteStream();
        await using var modem = new KissModem(stream);

        var frameA = new KissFrame(0x00, [0x11, 0x11, 0x11]);
        var frameB = new KissFrame(0x01, [0x22, 0x22, 0x22]);

        await Task.WhenAll(
            modem.SendAsync(frameA, TestContext.Current.CancellationToken),
            modem.SendAsync(frameB, TestContext.Current.CancellationToken));

        var written = stream.Written;

        // Each write call must be a complete KISS frame, never interleaved bytes.
        var frames = 0;
        var i = 0;
        while (i < written.Count)
        {
            Assert.Equal(0xC0, written[i]); // opening FEND
            var close = written.IndexOf(0xC0, i + 1);
            Assert.True(close > i, "Frame was not terminated by a FEND — sends interleaved.");
            frames++;
            i = close + 1;
        }

        Assert.Equal(2, frames);
    }

    // ---------------------------------------------------------------
    // Frames split across stream reads
    // ---------------------------------------------------------------

    [Fact]
    public async Task FrameReceived_EscapeSequenceSplitAcrossReads_IsUnescapedCorrectly()
    {
        using var stream = new BlockingDuplexStream();
        await using var modem = new KissModem(stream);

        var tcs = new TaskCompletionSource<KissFrame>();
        modem.FrameReceived += (_, frame) => tcs.TrySetResult(frame);

        modem.Start();

        // FEND + cmd(0x00) + FESC, then TFEND + FEND in the next chunk.
        stream.Enqueue([0xC0, 0x00, 0xDB]);
        stream.Enqueue([0xDC, 0xC0]);

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal([0xC0], received.Data);
    }

    [Fact]
    public async Task FrameReceived_ByteByByteDelivery_ReceivesFrame()
    {
        using var stream = new BlockingDuplexStream();
        await using var modem = new KissModem(stream);

        var tcs = new TaskCompletionSource<KissFrame>();
        modem.FrameReceived += (_, frame) => tcs.TrySetResult(frame);

        modem.Start();

        foreach (var b in (byte[])[0xC0, 0x00, 0x01, 0x02, 0xC0])
        {
            stream.Enqueue([b]);
        }

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal([0x01, 0x02], received.Data);
    }

    // ---------------------------------------------------------------
    // Oversized frame handling
    // ---------------------------------------------------------------

    [Fact]
    public async Task OversizedFrame_FiresReceiveError_AndResynchronizesOnNextFrame()
    {
        using var stream = new BlockingDuplexStream();
        await using var modem = new KissModem(stream);

        var errorTcs = new TaskCompletionSource<Exception>();
        modem.ReceiveError += (_, ex) => errorTcs.TrySetResult(ex);

        var frameTcs = new TaskCompletionSource<KissFrame>();
        modem.FrameReceived += (_, frame) => frameTcs.TrySetResult(frame);

        modem.Start();

        // Overlong frame with no FEND, followed by a valid frame.
        stream.Enqueue([0xC0, 0x00]);
        stream.Enqueue(new byte[5000]);
        stream.Enqueue([0xC0, 0x00, 0x01, 0xC0]);

        var error = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Contains("exceeds maximum length", error.Message);

        var received = await frameTcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal([0x01], received.Data);
    }

    // ---------------------------------------------------------------
    // Remote stream close
    // ---------------------------------------------------------------

    [Fact]
    public async Task ReadLoop_StreamClosedByRemote_ReadLoopExitsWithoutError()
    {
        using var stream = new CloseAfterDataStream([0xC0, 0x00, 0x01, 0xC0]);
        await using var modem = new KissModem(stream);

        var tcs = new TaskCompletionSource<KissFrame>();
        modem.FrameReceived += (_, frame) => tcs.TrySetResult(frame);

        var errorTcs = new TaskCompletionSource<Exception>();
        modem.ReceiveError += (_, ex) => errorTcs.TrySetResult(ex);

        modem.Start();

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal([0x01], received.Data);

        // Loop exits silently on remote close; the pending reads never fire.
        await modem.StopAsync();
        Assert.False(errorTcs.Task.IsCompleted, "Remote close should not raise a receive error.");
    }

    [Fact]
    public async Task StopAsync_AfterRemoteClose_CompletesAndAllowsRestart()
    {
        using var stream = new ImmediateEofStream();
        await using var modem = new KissModem(stream);

        modem.Start();
        // Let the read loop hit EOF and exit.
        await Task.Delay(200, TestContext.Current.CancellationToken);

        await modem.StopAsync(); // must not hang or throw

        modem.Start(); // restart must be possible after remote close
        await modem.StopAsync();
    }

    // ---------------------------------------------------------------
    // Send cancellation
    // ---------------------------------------------------------------

    [Fact]
    public async Task SendAsync_CancelledBeforeWrite_ThrowsOperationCanceled()
    {
        using var stream = new BlockingWriteStream();
        await using var modem = new KissModem(stream);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => modem.SendAsync(new KissFrame(0x00, [0x01]), cts.Token));
    }

    // ---------------------------------------------------------------
    // Fuzz: encode -> wire chunks -> receive round-trip
    // ---------------------------------------------------------------

    [Fact]
    public void ReferenceDecoder_OnSameWire_DecodesAllFrames()
    {
        var (expected, wire) = BuildFuzzWire(new Random(20261021), 300);

        var decoded = new List<(byte Command, byte[] Data)>();
        var current = new List<byte>();
        foreach (var b in wire)
        {
            if (b == KissConstants.FEND)
            {
                if (current.Count > 0)
                {
                    decoded.Add(DecodeReference(current));
                }

                current.Clear();
            }
            else
            {
                current.Add(b);
            }
        }

        Assert.Equal(expected.Count, decoded.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Command, decoded[i].Command);
            Assert.Equal(expected[i].Data, decoded[i].Data);
        }
    }

    private static (byte Command, byte[] Data) DecodeReference(List<byte> raw)
    {
        var unescaped = KissSerializer.UnescapeData([.. raw]);
        return (unescaped[0], unescaped[1..]);
    }

    private static (List<KissFrame> Expected, List<byte> Wire) BuildFuzzWire(Random random, int count)
    {
        byte[] commandNibbles = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x0F];

        var expected = new List<KissFrame>();
        var wire = new List<byte>();
        for (var n = 0; n < count; n++)
        {
            // 0xC0 (FEND) can never be a command byte on the wire; skip it.
            byte command;
            do
            {
                command = (byte)((random.Next(16) << 4) | commandNibbles[random.Next(commandNibbles.Length)]);
            }
            while (command is KissConstants.FEND or KissConstants.FESC);

            var data = new byte[random.Next(0, 64)];
            random.NextBytes(data);

            expected.Add(new KissFrame(command, data));
            wire.AddRange(KissSerializer.EncodeFrame(new KissFrame(command, data)));
        }

        return (expected, wire);
    }

    [Fact]
    public async Task MalformedWire_CommandByteFEND_ModemResynchronizesOnLaterValidFrame()
    {
        // Wire produced by EncodeFrame for a command byte of 0xC0 (FEND):
        // the serializer happily emits it raw, which corrupts framing. Whatever
        // the modem makes of the garbage, it must stay able to resync and
        // dispatch a later valid frame.
        using var stream = new BlockingDuplexStream();
        await using var modem = new KissModem(stream);

        var received = new System.Collections.Concurrent.ConcurrentQueue<KissFrame>();
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var validFrame = new TaskCompletionSource<KissFrame>();

        modem.FrameReceived += (_, frame) =>
        {
            received.Enqueue(frame);
            if (frame.Data.SequenceEqual(new byte[] { 0xAA, 0xBB }))
            {
                validFrame.TrySetResult(frame);
            }
        };

        modem.ReceiveError += (_, ex) => errors.Enqueue(ex);

        modem.Start();

        // Malformed burst: frames with command byte 0xC0 (raw FEND on the wire),
        // i.e. exactly what the wire looks like if a sender emits such bytes.
        for (var n = 0; n < 20; n++)
        {
            stream.Enqueue([0xC0, 0xC0, 0x11, 0x22, 0x33, 0xC0]);
        }

        // Give the modem time to chew through the garbage, then send a valid frame.
        await Task.Delay(500, TestContext.Current.CancellationToken);
        stream.Enqueue([0xC0, 0x00, 0xAA, 0xBB, 0xC0]);

        var resynced = await validFrame.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal([0xAA, 0xBB], resynced.Data);
    }

    [Fact]
    public async Task Fuzz_RandomFrames_RoundTripThroughWireChunks()
    {
        using var stream = new BlockingDuplexStream();
        await using var modem = new KissModem(stream);

        var random = new Random(20261021);
        var (expected, wire) = BuildFuzzWire(random, 300);

        var received = new System.Collections.Concurrent.ConcurrentQueue<KissFrame>();
        var allReceived = new TaskCompletionSource<bool>();
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        modem.FrameReceived += (_, frame) =>
        {
            received.Enqueue(frame);
            if (received.Count >= expected.Count)
            {
                allReceived.TrySetResult(true);
            }
        };

        modem.ReceiveError += (_, ex) =>
        {
            errors.Enqueue(ex);
            allReceived.TrySetException(
                new InvalidOperationException(
                    $"Receive error after {received.Count} frames; errors: {errors.Count}. First: {ex}", ex));
        };

        modem.Start();

        // Chop the wire bytes into random chunks to simulate stream fragmentation.
        var offset = 0;
        while (offset < wire.Count)
        {
            var size = Math.Min(random.Next(1, 40), wire.Count - offset);
            stream.Enqueue([.. wire.GetRange(offset, size)]);
            offset += size;
        }

        try
        {
            await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            Assert.Fail(
                $"Timed out: received={received.Count}, expected={expected.Count}, errors={errors.Count}, " +
                $"firstError={(errors.TryPeek(out var first) ? first.Message : "none")}");
        }

        Assert.Equal(expected.Count, received.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.True(
                received.TryDequeue(out var frame),
                $"Frame {i} missing.");
            Assert.Equal(expected[i].Command, frame.Command);
            Assert.Equal(expected[i].Data, frame.Data);
        }
    }

    // ---------------------------------------------------------------
    // Helper streams
    // ---------------------------------------------------------------

    /// <summary>Duplex stream whose reads block until data is enqueued.</summary>
    private sealed class BlockingDuplexStream : Stream
    {
        private readonly Queue<byte[]> _pending = [];
        private readonly SemaphoreSlim _dataAvailable = new(0, int.MaxValue);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void Enqueue(byte[] data)
        {
            lock (_pending)
            {
                _pending.Enqueue(data);
            }

            _dataAvailable.Release();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _dataAvailable.WaitAsync(cancellationToken).ConfigureAwait(false);

            byte[] chunk;
            lock (_pending)
            {
                chunk = _pending.Dequeue();
            }

            var toCopy = Math.Min(buffer.Length, chunk.Length);
            chunk.AsSpan(0, toCopy).CopyTo(buffer.Span);
            if (toCopy < chunk.Length)
            {
                lock (_pending)
                {
                    _pending.Enqueue(chunk[toCopy..]);
                }

                _dataAvailable.Release();
            }

            return toCopy;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var memory = new Memory<byte>(buffer, offset, count);
            return ReadAsync(memory).AsTask().GetAwaiter().GetResult();
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }

    /// <summary>Stream whose writes are delayed so concurrent sends overlap.</summary>
    private sealed class SlowWriteStream : Stream
    {
        private readonly List<byte> _written = [];

        public List<byte> Written
        {
            get
            {
                lock (_written)
                {
                    return [.. _written];
                }
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            lock (_written)
            {
                _written.AddRange(buffer.Span);
            }
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Stream whose writes block until released — used to test send cancellation.</summary>
    private sealed class BlockingWriteStream : Stream
    {
        private readonly SemaphoreSlim _gate = new(0, 1);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Stream that delivers one chunk then reports EOF (0 bytes) forever.</summary>
    private sealed class CloseAfterDataStream(byte[] data) : Stream
    {
        private bool _delivered;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_delivered)
            {
                _delivered = true;
                var toCopy = Math.Min(buffer.Length, data.Length);
                data.AsSpan(0, toCopy).CopyTo(buffer.Span);
                return ValueTask.FromResult(toCopy);
            }

            return ValueTask.FromResult(0);
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }

    /// <summary>Stream that immediately reports EOF.</summary>
    private sealed class ImmediateEofStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }
}

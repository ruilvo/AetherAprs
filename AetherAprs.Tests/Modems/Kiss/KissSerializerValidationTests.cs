// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Modems.Kiss;
using Xunit;

namespace AetherAprs.Tests.Modems.Kiss;

/// <summary>
/// Validation tests for command-byte handling in the KISS serializer and modem.
/// These encode the desired behavior for command bytes that collide with
/// KISS control characters (FEND 0xC0, FESC 0xDB).
/// </summary>
public class KissSerializerValidationTests
{
    // ---------------------------------------------------------------
    // Bug: EncodeFrame emits a raw FEND when the command byte is 0xC0,
    // producing a frame that cannot exist on the wire.
    // ---------------------------------------------------------------

    [Fact]
    public void EncodeFrame_CommandByteFEND_ThrowsArgumentException()
    {
        // A command byte of 0xC0 IS a FEND: EncodeFrame would emit it raw
        // and the resulting frame is unrepresentable/corrupt on the wire.
        Assert.Throws<ArgumentException>(
            () => KissSerializer.EncodeFrame(new KissFrame(0xC0, [0x01])));
    }

    // ---------------------------------------------------------------
    // Bug: command byte 0xDB (FESC) is emitted unescaped while the
    // receive path unescapes the frame INCLUDING the command byte,
    // so a modem cannot round-trip its own encoding.
    // ---------------------------------------------------------------

    [Fact]
    public async Task EncodeFrame_CommandByteFESC_RoundTripsThroughOwnModem()
    {
        // The command byte is transmitted unescaped per the KISS spec, so a
        // frame with a 0xDB (FESC) command byte must round-trip unchanged.
        // The receive path must therefore unescape only the payload.
        using var stream = new BlockingDuplexStream();
        await using var modem = new KissModem(stream);

        var tcs = new TaskCompletionSource<KissFrame>();
        modem.FrameReceived += (_, frame) => tcs.TrySetResult(frame);

        modem.Start();

        stream.Enqueue(KissSerializer.EncodeFrame(new KissFrame(0xDB, [0xDC])));

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal(0xDB, received.Command);
        Assert.Equal([0xDC], received.Data);
    }

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
}

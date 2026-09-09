// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Modems.Kiss;
using Xunit;

namespace AetherAprs.Tests.Kiss;

public class KissModemTests
{
    // ---------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------

    [Fact]
    public void Constructor_NullStream_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new KissModem(null!));
    }

    [Fact]
    public void Constructor_NonReadableStream_ThrowsArgumentException()
    {
        using var stream = new NonReadableStream();

        var exception = Assert.Throws<ArgumentException>(() => new KissModem(stream));

        Assert.Contains("stream", exception.Message);
    }

    [Fact]
    public void Constructor_NonWritableStream_ThrowsArgumentException()
    {
        using var stream = new NonWritableStream();

        var exception = Assert.Throws<ArgumentException>(() => new KissModem(stream));

        Assert.Contains("stream", exception.Message);
    }

    // ---------------------------------------------------------------
    // SendAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task SendAsync_EmptyDataFrame_WritesEncodedFrame()
    {
        using var stream = new MemoryStream();
        await using var modem = new KissModem(stream);

        var frame = new KissFrame(0x00, []);
        await modem.SendAsync(frame);

        // FEND + command(0x00) + FEND = 3 bytes
        var written = stream.ToArray();
        Assert.Equal(3, written.Length);
        Assert.Equal(0xC0, written[0]);
        Assert.Equal(0x00, written[1]);
        Assert.Equal(0xC0, written[2]);
    }

    [Fact]
    public async Task SendAsync_DataFrameWithPayload_WritesEscapedFrame()
    {
        using var stream = new MemoryStream();
        await using var modem = new KissModem(stream);

        var frame = new KissFrame(0x00, [0x01, 0x02, 0x03]);
        await modem.SendAsync(frame);

        // FEND + cmd + 3 bytes data + FEND = 6
        var written = stream.ToArray();
        Assert.Equal(6, written.Length);
        Assert.Equal(0xC0, written[0]);
        Assert.Equal(0x00, written[1]);
        Assert.Equal(0x01, written[2]);
        Assert.Equal(0x02, written[3]);
        Assert.Equal(0x03, written[4]);
        Assert.Equal(0xC0, written[5]);
    }

    [Fact]
    public async Task SendAsync_DataWithFEND_FrameEscapesTheBytes()
    {
        using var stream = new MemoryStream();
        await using var modem = new KissModem(stream);

        var frame = new KissFrame(0x00, [0xC0]);
        await modem.SendAsync(frame);

        // FEND + cmd + FESC(0xDB) + TFEND(0xDC) + FEND = 5
        var written = stream.ToArray();
        Assert.Equal(5, written.Length);
        Assert.Equal(0xC0, written[0]);
        Assert.Equal(0x00, written[1]);
        Assert.Equal(0xDB, written[2]);
        Assert.Equal(0xDC, written[3]);
        Assert.Equal(0xC0, written[4]);
    }

    [Fact]
    public async Task SendAsync_DataWithFESC_FrameEscapesTheBytes()
    {
        using var stream = new MemoryStream();
        await using var modem = new KissModem(stream);

        var frame = new KissFrame(0x00, [0xDB]);
        await modem.SendAsync(frame);

        // FEND + cmd + FESC(0xDB) + TFESC(0xDD) + FEND = 5
        var written = stream.ToArray();
        Assert.Equal(5, written.Length);
        Assert.Equal(0xC0, written[0]);
        Assert.Equal(0x00, written[1]);
        Assert.Equal(0xDB, written[2]);
        Assert.Equal(0xDD, written[3]);
        Assert.Equal(0xC0, written[4]);
    }

    [Fact]
    public async Task SendAsync_TXDelayCommand_EncodesCommandByte()
    {
        using var stream = new MemoryStream();
        await using var modem = new KissModem(stream);

        var frame = new KissFrame((byte)KissCommandType.TXDelay, [0x05]);
        await modem.SendAsync(frame);

        var written = stream.ToArray();
        Assert.Equal(0x01, written[1]); // TXDelay = 0x01
        Assert.Equal(0x05, written[2]);
    }

    [Fact]
    public async Task SendAsync_PortInUpperBits_CommandBytePreservesPort()
    {
        using var stream = new MemoryStream();
        await using var modem = new KissModem(stream);

        var frame = new KissFrame(0x30, [0x01]); // port 3, DataFrame
        await modem.SendAsync(frame);

        var written = stream.ToArray();
        Assert.Equal(0x30, written[1]);
    }

    [Fact]
    public async Task SendAsync_FlushesStreamAfterWrite()
    {
        using var stream = new MemoryStream();
        await using var modem = new KissModem(stream);

        var frame = new KissFrame(0x00, [0x01]);
        await modem.SendAsync(frame);

        // For MemoryStream, ToArray() should return all data after flush
        Assert.Equal(4, stream.Length); // FEND + cmd + 1 data byte + FEND
    }

    // ---------------------------------------------------------------
    // SendAsync - disposed
    // ---------------------------------------------------------------

    [Fact]
    public async Task SendAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        using var stream = new MemoryStream();
        var modem = new KissModem(stream);
        await modem.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => modem.SendAsync(new KissFrame(0x00, [])));
    }

    // ---------------------------------------------------------------
    // Start / Stop lifecycle
    // ---------------------------------------------------------------

    [Fact]
    public async Task Start_FirstCall_Succeeds()
    {
        await using var stream = new MemoryStream();
        await using var modem = new KissModem(stream);

        modem.Start();

        // No exception is the assertion
    }

    [Fact]
    public async Task Start_DoubleCall_ThrowsInvalidOperationException()
    {
        await using var stream = new MemoryStream();
        await using var modem = new KissModem(stream);

        modem.Start();

        Assert.Throws<InvalidOperationException>(() => modem.Start());
    }

    [Fact]
    public async Task StopAsync_WithoutStart_IsNoOp()
    {
        using var stream = new MemoryStream();
        await using var modem = new KissModem(stream);

        // Should not throw
        await modem.StopAsync();
    }

    [Fact]
    public async Task StartThenStopAsync_CompletesCleanly()
    {
        using var stream = new MemoryStream();
        await using var modem = new KissModem(stream);

        modem.Start();
        await modem.StopAsync();

        // Can start again after stop
        modem.Start();
        await modem.StopAsync();
    }

    // ---------------------------------------------------------------
    // FrameReceived
    // ---------------------------------------------------------------

    [Fact]
    public async Task FrameReceived_SingleDataFrame_ReceivesCorrectFrame()
    {
        // FEND + cmd(0x00) + data(0x01) + FEND
        using var stream = new MemoryStream([0xC0, 0x00, 0x01, 0xC0]);
        await using var modem = new KissModem(stream);

        var tcs = new TaskCompletionSource<KissFrame>();
        modem.FrameReceived += (_, frame) => tcs.TrySetResult(frame);

        modem.Start();

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(0x00, received.Command);
        Assert.Equal([0x01], received.Data);
        Assert.Equal(KissCommandType.DataFrame, received.CommandType);
    }

    [Fact]
    public async Task FrameReceived_EmptyPayload_ReceivesFrameWithNoData()
    {
        using var stream = new MemoryStream([0xC0, 0x00, 0xC0]);
        await using var modem = new KissModem(stream);

        var tcs = new TaskCompletionSource<KissFrame>();
        modem.FrameReceived += (_, frame) => tcs.TrySetResult(frame);

        modem.Start();

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(0x00, received.Command);
        Assert.Empty(received.Data);
    }

    [Fact]
    public async Task FrameReceived_FENDInPayload_ReceivesUnescapedData()
    {
        // FEND + cmd + FESC(0xDB) + TFEND(0xDC) + FEND
        using var stream = new MemoryStream([0xC0, 0x00, 0xDB, 0xDC, 0xC0]);
        await using var modem = new KissModem(stream);

        var tcs = new TaskCompletionSource<KissFrame>();
        modem.FrameReceived += (_, frame) => tcs.TrySetResult(frame);

        modem.Start();

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // The data byte 0xC0 should be restored from the escape sequence
        Assert.Equal([0xC0], received.Data);
    }

    [Fact]
    public async Task FrameReceived_FESCInPayload_ReceivesUnescapedData()
    {
        // FEND + cmd + FESC(0xDB) + TFESC(0xDD) + FEND
        using var stream = new MemoryStream([0xC0, 0x00, 0xDB, 0xDD, 0xC0]);
        await using var modem = new KissModem(stream);

        var tcs = new TaskCompletionSource<KissFrame>();
        modem.FrameReceived += (_, frame) => tcs.TrySetResult(frame);

        modem.Start();

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // The data byte 0xDB should be restored from the escape sequence
        Assert.Equal([0xDB], received.Data);
    }

    [Fact]
    public async Task FrameReceived_MultipleFramesInOneRead_ReceivesAll()
    {
        // Two frames: [FEND 0x00 0x01 FEND] [FEND 0x01 0x05 FEND]
        using var stream = new MemoryStream([0xC0, 0x00, 0x01, 0xC0, 0xC0, 0x01, 0x05, 0xC0]);
        await using var modem = new KissModem(stream);

        var receivedFrames = new System.Collections.Generic.List<KissFrame>();
        modem.FrameReceived += (_, frame) => receivedFrames.Add(frame);

        modem.Start();

        // Wait for the read loop to process all data
        await Task.Delay(500);

        Assert.Equal(2, receivedFrames.Count);
        Assert.Equal(0x00, receivedFrames[0].Command);
        Assert.Equal([0x01], receivedFrames[0].Data);
        Assert.Equal(0x01, receivedFrames[1].Command);
        Assert.Equal([0x05], receivedFrames[1].Data);
    }

    [Fact]
    public async Task FrameReceived_BackToBackFrames_NoSpuriousEmptyFrame()
    {
        // FEND + data(0x01) + FEND FEND + data(0x02) + FEND
        // The FEND-FEND boundary should not create an empty frame
        using var stream = new MemoryStream([0xC0, 0x00, 0x01, 0xC0, 0xC0, 0x00, 0x02, 0xC0]);
        await using var modem = new KissModem(stream);

        var receivedFrames = new System.Collections.Generic.List<KissFrame>();
        modem.FrameReceived += (_, frame) => receivedFrames.Add(frame);

        modem.Start();

        await Task.Delay(500);

        Assert.Equal(2, receivedFrames.Count);
        Assert.Equal([0x01], receivedFrames[0].Data);
        Assert.Equal([0x02], receivedFrames[1].Data);
    }

    [Fact]
    public async Task FrameReceived_NonZeroPort_ExtractsPort()
    {
        // Command = 0x31 → port 3 (upper nibble), TXDelay (lower nibble)
        using var stream = new MemoryStream([0xC0, 0x31, 0x05, 0xC0]);
        await using var modem = new KissModem(stream);

        var tcs = new TaskCompletionSource<KissFrame>();
        modem.FrameReceived += (_, frame) => tcs.TrySetResult(frame);

        modem.Start();

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(KissCommandType.TXDelay, received.CommandType);
        Assert.Equal(3, received.Port);
    }

    [Fact]
    public async Task FrameReceived_ReturnCommand_ReceivesCommand()
    {
        // Return (0xFF) uses all 8 bits. The lower 4 bits extract 0x0F = 15,
        // and port extracts upper nibble = 15.
        await using var stream = new MemoryStream([0xC0, 0xFF, 0xC0]);
        await using var modem = new KissModem(stream);

        var tcs = new TaskCompletionSource<KissFrame>();
        modem.FrameReceived += (_, frame) => tcs.TrySetResult(frame);

        modem.Start();

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal((KissCommandType)15, received.CommandType);
        Assert.Equal(15, received.Port);
    }

    [Fact]
    public async Task FrameReceived_ConsecutiveFENDWithoutPayload_SkipsEmptyFrame()
    {
        // Two FENDs with nothing between them should not generate a frame
        using var stream = new MemoryStream([0xC0, 0xC0, 0xC0, 0x00, 0x01, 0xC0]);
        await using var modem = new KissModem(stream);

        var receivedFrames = new System.Collections.Generic.List<KissFrame>();
        modem.FrameReceived += (_, frame) => receivedFrames.Add(frame);

        modem.Start();

        await Task.Delay(500);

        // Only one frame should be received (with cmd=0x00, data=[0x01])
        Assert.Single(receivedFrames);
        Assert.Equal(0x00, receivedFrames[0].Command);
    }

    // ---------------------------------------------------------------
    // ReceiveError
    // ---------------------------------------------------------------

    [Fact]
    public async Task ReceiveError_InvalidEscapeSequence_FiresErrorEvent()
    {
        // FEND + cmd + FESC(0xDB) + 0x42(invalid) + FEND
        using var stream = new MemoryStream([0xC0, 0x00, 0xDB, 0x42, 0xC0]);
        await using var modem = new KissModem(stream);

        var tcs = new TaskCompletionSource<Exception>();
        modem.ReceiveError += (_, ex) => tcs.TrySetResult(ex);

        modem.Start();

        var error = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.IsType<ArgumentException>(error);
        Assert.Contains("Invalid escape sequence", error.Message);
    }

    [Fact]
    public async Task ReceiveError_IncompleteEscapeSequence_FiresErrorEvent()
    {
        // FEND + cmd + FESC(0xDB) + FEND (incomplete escape at end)
        using var stream = new MemoryStream([0xC0, 0x00, 0xDB, 0xC0]);
        await using var modem = new KissModem(stream);

        var tcs = new TaskCompletionSource<Exception>();
        modem.ReceiveError += (_, ex) => tcs.TrySetResult(ex);

        modem.Start();

        var error = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.IsType<ArgumentException>(error);
        Assert.Contains("Incomplete escape sequence", error.Message);
    }

    [Fact]
    public async Task ReceiveError_ReadError_FiresErrorEvent()
    {
        using var stream = new FailingStream();
        await using var modem = new KissModem(stream);

        var tcs = new TaskCompletionSource<Exception>();
        modem.ReceiveError += (_, ex) => tcs.TrySetResult(ex);

        modem.Start();

        var error = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.IsType<IOException>(error);
        Assert.Contains("simulated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------
    // ReceiveError - FrameReceived not fired for corrupt frames
    // ---------------------------------------------------------------

    [Fact]
    public async Task InvalidEscapeSequence_FrameReceivedNotFired()
    {
        using var stream = new MemoryStream([0xC0, 0x00, 0xDB, 0x42, 0xC0]);
        await using var modem = new KissModem(stream);

        var frameReceived = false;
        modem.FrameReceived += (_, _) => frameReceived = true;

        var errorTcs = new TaskCompletionSource<Exception>();
        modem.ReceiveError += (_, ex) => errorTcs.TrySetResult(ex);

        modem.Start();

        await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(frameReceived);
    }

    // ---------------------------------------------------------------
    // DisposeAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_StopsReadLoop()
    {
        using var stream = new MemoryStream();
        var modem = new KissModem(stream);

        modem.Start();
        await modem.DisposeAsync();

        // After dispose, the read task should complete
        Assert.True(true); // No hang = success
    }

    [Fact]
    public async Task DisposeAsync_Idempotent_CanBeCalledMultipleTimes()
    {
        using var stream = new MemoryStream();
        var modem = new KissModem(stream);

        await modem.DisposeAsync();
        await modem.DisposeAsync(); // Should not throw

        Assert.True(true);
    }

    // ---------------------------------------------------------------
    // Helper stream types
    // ---------------------------------------------------------------

    private sealed class NonReadableStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }

    private sealed class NonWritableStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FailingStream : Stream
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
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new IOException("Simulated read failure.");
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }
}
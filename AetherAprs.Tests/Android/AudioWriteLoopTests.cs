// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;

using AetherAprs.Android.Services.Platform;
using AetherAprs.Services.Platform;

using Xunit;

namespace AetherAprs.Tests.Android;

/// <summary>
/// Regression tests: a device may accept fewer audio samples than requested and
/// may return fewer than the buffer capacity; the transfer loop must not
/// truncate a frame or spin on a zero-length transfer.
/// </summary>
public sealed class AudioWriteLoopTests
{
    [Fact]
    public void WriteAll_AcceptsWholeBufferInOneCall_Completes()
    {
        byte[] buffer = new byte[16];
        int calls = 0;

        AudioTransferResult result = AudioWriteLoop.WriteAll(
            buffer.Length,
            (offset, count) =>
            {
                calls++;
                Array.Fill(buffer, (byte)1, offset, count);
                return count;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AudioTransferStatus.Completed, result.Status);
        Assert.True(result.IsComplete);
        Assert.Equal(buffer.Length, result.Transferred);
        Assert.Equal(1, calls);
        Assert.All(buffer, value => Assert.Equal(1, value));
    }

    [Fact]
    public void WriteAll_PartialWrites_RetriesUntilWholeBufferIsWritten()
    {
        byte[] buffer = new byte[10];
        int calls = 0;

        AudioTransferResult result = AudioWriteLoop.WriteAll(
            buffer.Length,
            (offset, count) =>
            {
                calls++;
                int written = Math.Min(count, 3);
                Array.Fill(buffer, (byte)2, offset, written);
                return written;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AudioTransferStatus.Completed, result.Status);
        Assert.Equal(buffer.Length, result.Transferred);
        Assert.Equal(4, calls);
        Assert.All(buffer, value => Assert.Equal(2, value));
    }

    [Fact]
    public void WriteAll_ZeroByteWrite_StopsWithoutProgress()
    {
        int calls = 0;

        AudioTransferResult result = AudioWriteLoop.WriteAll(
            12,
            (offset, count) =>
            {
                calls++;
                return 0;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AudioTransferStatus.ZeroLength, result.Status);
        Assert.Equal(0, result.Transferred);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void WriteAll_NegativeWrite_Fails()
    {
        AudioTransferResult result = AudioWriteLoop.WriteAll(
            12,
            (offset, count) => -3,
            TestContext.Current.CancellationToken);

        Assert.Equal(AudioTransferStatus.Failed, result.Status);
        Assert.Equal(0, result.Transferred);
    }

    [Fact]
    public void WriteAll_GuardBlocksBeforeFirstChunk_TransfersNothing()
    {
        int calls = 0;

        AudioTransferResult result = AudioWriteLoop.WriteAll(
            8,
            (offset, count) =>
            {
                calls++;
                return count;
            },
            () => AudioTransmitBlockReason.FocusLost,
            TestContext.Current.CancellationToken);

        Assert.Equal(AudioTransferStatus.Blocked, result.Status);
        Assert.Equal(AudioTransmitBlockReason.FocusLost, result.BlockReason);
        Assert.Equal(0, result.Transferred);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void WriteAll_GuardBlocksAfterPartialWrite_StopsMidFrame()
    {
        int calls = 0;
        int guardCalls = 0;

        AudioTransferResult result = AudioWriteLoop.WriteAll(
            12,
            (offset, count) =>
            {
                calls++;
                return Math.Min(count, 4);
            },
            () => ++guardCalls == 1
                ? AudioTransmitBlockReason.None
                : AudioTransmitBlockReason.RouteBecomingNoisy,
            TestContext.Current.CancellationToken);

        Assert.Equal(AudioTransferStatus.Blocked, result.Status);
        Assert.Equal(AudioTransmitBlockReason.RouteBecomingNoisy, result.BlockReason);
        Assert.Equal(4, result.Transferred);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void WriteAll_Guard_RevalidatesBeforeEveryChunk()
    {
        int guardCalls = 0;

        AudioTransferResult result = AudioWriteLoop.WriteAll(
            9,
            (offset, count) => Math.Min(count, 3),
            () =>
            {
                guardCalls++;
                return AudioTransmitBlockReason.None;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AudioTransferStatus.Completed, result.Status);
        Assert.Equal(9, result.Transferred);
        Assert.Equal(3, guardCalls);
    }

    [Fact]
    public void WriteAll_CancelledBeforeBlockedGuard_ReportsAbortion()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        AudioTransferResult result = AudioWriteLoop.WriteAll(
            8,
            (offset, count) => count,
            () => AudioTransmitBlockReason.FocusLost,
            cancellation.Token);

        // Cancellation is the caller's own intent and wins over the route check, so a
        // cancelled transmission is not reported as a route failure.
        Assert.Equal(AudioTransferStatus.Aborted, result.Status);
    }

    [Fact]
    public void WriteAll_AbortedBetweenChunks_ReturnsAbortedWithProgress()
    {
        using CancellationTokenSource cancellation = new();
        int calls = 0;

        AudioTransferResult result = AudioWriteLoop.WriteAll(
            10,
            (offset, count) =>
            {
                calls++;
                cancellation.Cancel();
                return 2;
            },
            cancellation.Token);

        Assert.Equal(AudioTransferStatus.Aborted, result.Status);
        Assert.Equal(2, result.Transferred);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ReadUpTo_ShortReads_AccumulatesUntilCapacity()
    {
        int calls = 0;

        AudioTransferResult result = AudioWriteLoop.ReadUpTo(
            9,
            (offset, count) =>
            {
                calls++;
                return Math.Min(count, 4);
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AudioTransferStatus.Completed, result.Status);
        Assert.Equal(9, result.Transferred);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void ReadUpTo_ReadEndsAfterPartialChunk_ReturnsWhatWasRead()
    {
        int calls = 0;

        AudioTransferResult result = AudioWriteLoop.ReadUpTo(
            8,
            (offset, count) =>
            {
                calls++;
                return calls == 1 ? 3 : 0;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(AudioTransferStatus.Completed, result.Status);
        Assert.Equal(3, result.Transferred);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ReadUpTo_ZeroFirstRead_StopsWithoutProgress()
    {
        AudioTransferResult result = AudioWriteLoop.ReadUpTo(
            8,
            (offset, count) => 0,
            TestContext.Current.CancellationToken);

        Assert.Equal(AudioTransferStatus.ZeroLength, result.Status);
        Assert.Equal(0, result.Transferred);
    }

    [Fact]
    public void ReadUpTo_NegativeRead_Fails()
    {
        AudioTransferResult result = AudioWriteLoop.ReadUpTo(
            8,
            (offset, count) => -1,
            TestContext.Current.CancellationToken);

        Assert.Equal(AudioTransferStatus.Failed, result.Status);
        Assert.Equal(0, result.Transferred);
    }
}

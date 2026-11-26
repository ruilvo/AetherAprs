// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Services.Bluetooth;
using Xunit;

namespace AetherAprs.Tests.Services.Bluetooth;

public sealed class BluetoothLeFrameWriterTests
{
    [Fact]
    public async Task WriteAsync_WithTheDefaultMtu_SlicesTheFrameByPayloadSize()
    {
        FakeBluetoothLeTransport transport = new(payloadSize: 20);
        BluetoothLeFrameWriter writer = CreateWriter(transport);

        await writer.WriteAsync(Frame(50), TestContext.Current.CancellationToken);

        Assert.Equal(3, transport.Attempts);
        Assert.Equal(new[] { 20, 20, 10 }, transport.ChunkLengths());
        Assert.Equal(50, transport.BytesWritten());
    }

    [Fact]
    public async Task WriteAsync_WithANegotiatedMtu_WritesTheFrameInOneChunk()
    {
        FakeBluetoothLeTransport transport = new(payloadSize: 244);
        BluetoothLeFrameWriter writer = CreateWriter(transport);

        await writer.WriteAsync(Frame(200), TestContext.Current.CancellationToken);

        Assert.Equal(1, transport.Attempts);
        Assert.Equal(new[] { 200 }, transport.ChunkLengths());
    }

    [Fact]
    public async Task WriteAsync_WhenTheStackIsBusy_RetriesTheSameChunk()
    {
        FakeBluetoothLeTransport transport = new(payloadSize: 20);
        transport.Enqueue(GattWriteOutcome.Busy, GattWriteOutcome.Busy);
        BluetoothLeFrameWriter writer = CreateWriter(transport);

        await writer.WriteAsync(Frame(10), TestContext.Current.CancellationToken);

        Assert.Equal(3, transport.Attempts);
        Assert.All(transport.ChunkLengths(), length => Assert.Equal(10, length));
    }

    [Fact]
    public async Task WriteAsync_WhenTheStackStaysBusy_ThrowsAfterTheBoundedAttempts()
    {
        FakeBluetoothLeTransport transport = new(payloadSize: 20);
        transport.Enqueue(
            GattWriteOutcome.Busy,
            GattWriteOutcome.Busy,
            GattWriteOutcome.Busy,
            GattWriteOutcome.Busy);
        BluetoothLeFrameWriter writer = CreateWriter(transport);

        IOException thrown = await Assert.ThrowsAsync<IOException>(async () =>
            await writer.WriteAsync(Frame(10), TestContext.Current.CancellationToken));

        Assert.Contains("Busy", thrown.Message);
        Assert.Equal(BluetoothLeFrameWriter.DefaultMaxAttempts, transport.Attempts);
    }

    [Fact]
    public async Task WriteAsync_WhenTheStackRejectsTheChunk_DoesNotRetry()
    {
        FakeBluetoothLeTransport transport = new(payloadSize: 20);
        transport.Enqueue(GattWriteOutcome.Rejected);
        BluetoothLeFrameWriter writer = CreateWriter(transport);

        await Assert.ThrowsAsync<IOException>(async () =>
            await writer.WriteAsync(Frame(10), TestContext.Current.CancellationToken));

        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task WriteAsync_WhenTheStackConnectsAndFailsTransiently_RetriesTheConnectionError()
    {
        FakeBluetoothLeTransport transport = new(payloadSize: 20);
        transport.Enqueue(GattWriteOutcome.ConnectionError);
        BluetoothLeFrameWriter writer = CreateWriter(transport);

        await writer.WriteAsync(Frame(10), TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.Attempts);
    }

    [Fact]
    public async Task WriteAsync_WhenTheChunkIsAbandoned_StopsWritingTheRestOfTheFrame()
    {
        FakeBluetoothLeTransport transport = new(payloadSize: 20);
        transport.BlockUntilReleased();
        BluetoothLeFrameWriter writer = CreateWriter(transport);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        Task write = writer.WriteAsync(Frame(100), cancellation.Token);
        Assert.False(write.IsCompleted);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await write);
        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task WriteAsync_WhenTheChunkIsLate_ContinuesAfterTheCallbackArrives()
    {
        FakeBluetoothLeTransport transport = new(payloadSize: 30);
        transport.BlockUntilReleased();
        BluetoothLeFrameWriter writer = CreateWriter(transport);

        Task write = writer.WriteAsync(Frame(60), TestContext.Current.CancellationToken);
        Assert.False(write.IsCompleted);

        transport.Release();
        await write;

        Assert.Equal(new[] { 30, 30 }, transport.ChunkLengths());
    }

    [Fact]
    public async Task WriteAsync_PassesTheWriteModeToTheTransport()
    {
        FakeBluetoothLeTransport withResponse = new(payloadSize: 512, supportsWithoutResponse: false);
        FakeBluetoothLeTransport withoutResponse = new(payloadSize: 512, supportsWithoutResponse: true);

        await CreateWriter(withResponse).WriteAsync(Frame(8), TestContext.Current.CancellationToken);
        await CreateWriter(withoutResponse).WriteAsync(Frame(8), TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(withResponse.Modes));
        Assert.True(Assert.Single(withoutResponse.Modes));
    }

    [Fact]
    public async Task WriteAsync_WhenTheFrameIsEmpty_WritesNothing()
    {
        FakeBluetoothLeTransport transport = new();

        await CreateWriter(transport).WriteAsync(ReadOnlyMemory<byte>.Empty, TestContext.Current.CancellationToken);

        Assert.Equal(0, transport.Attempts);
    }

    [Fact]
    public async Task WriteAsync_WhenTheTransportHasNoPayloadSize_Throws()
    {
        FakeBluetoothLeTransport transport = new(payloadSize: 0);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateWriter(transport).WriteAsync(Frame(4), TestContext.Current.CancellationToken));
    }

    private static BluetoothLeFrameWriter CreateWriter(FakeBluetoothLeTransport transport) =>
        new(transport, retryDelay: TimeSpan.Zero, pacingDelay: TimeSpan.Zero);

    private static byte[] Frame(int length)
    {
        byte[] frame = new byte[length];
        for (int index = 0; index < length; index++)
        {
            frame[index] = (byte)index;
        }

        return frame;
    }

    /// <summary>
    /// Stands in for the GATT characteristic: it records the chunks it was asked
    /// to write, replays scripted stack statuses, and can hold a write open so a
    /// late callback or a cancellation can be exercised.
    /// </summary>
    private sealed class FakeBluetoothLeTransport(
        int payloadSize = 20,
        bool supportsWithoutResponse = false) : IBluetoothLeChunkWriter
    {
        private readonly Queue<GattWriteOutcome> _outcomes = new();
        private readonly List<byte[]> _chunks = [];
        private TaskCompletionSource? _gate;

        public bool SupportsWriteWithoutResponse { get; } = supportsWithoutResponse;

        public int PayloadSize { get; } = payloadSize;

        public List<bool> Modes { get; } = [];

        public int Attempts => _chunks.Count;

        public void Enqueue(params GattWriteOutcome[] outcomes)
        {
            foreach (GattWriteOutcome outcome in outcomes)
            {
                _outcomes.Enqueue(outcome);
            }
        }

        public void BlockUntilReleased() =>
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate?.TrySetResult();

        public async Task<GattWriteOutcome> WriteChunkAsync(
            ReadOnlyMemory<byte> chunk,
            bool withoutResponse,
            CancellationToken cancellationToken)
        {
            _chunks.Add(chunk.ToArray());
            Modes.Add(withoutResponse);

            if (_gate is not null)
            {
                await _gate.Task.WaitAsync(cancellationToken);
            }

            return _outcomes.Count > 0 ? _outcomes.Dequeue() : GattWriteOutcome.Success;
        }

        public int[] ChunkLengths() => [.. _chunks.ConvertAll(chunk => chunk.Length)];

        public int BytesWritten()
        {
            int total = 0;
            foreach (byte[] chunk in _chunks)
            {
                total += chunk.Length;
            }

            return total;
        }
    }
}

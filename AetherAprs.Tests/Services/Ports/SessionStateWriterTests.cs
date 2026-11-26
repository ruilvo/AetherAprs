// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Services.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class SessionStateWriterTests
{
    [Fact]
    public async Task BurstOfRequests_IsCoalescedIntoTheNewestSnapshot()
    {
        GatedStateStore store = new(blockFirstSave: true);
        await using SessionStateWriter writer = new(store, NullLogger.Instance);
        writer.Start();

        writer.Request(CreateSnapshot(1));
        await store.FirstSaveEntered;

        writer.Request(CreateSnapshot(2));
        writer.Request(CreateSnapshot(3));
        store.ReleaseGate();

        await writer.FlushAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<SessionSnapshot> written = store.Written;
        Assert.Equal(2, written.Count);
        Assert.Equal(1, Marker(written[0]));
        Assert.Equal(3, Marker(written[^1]));
    }

    [Fact]
    public async Task FlushAsync_CompletesOnlyAfterTheSnapshotIsWritten()
    {
        GatedStateStore store = new(blockFirstSave: true);
        await using SessionStateWriter writer = new(store, NullLogger.Instance);
        writer.Start();

        writer.Request(CreateSnapshot(1));
        await store.FirstSaveEntered;

        Task flush = writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.False(flush.IsCompleted);
        Assert.Empty(store.Written);

        store.ReleaseGate();
        await flush;

        Assert.Single(store.Written);
    }

    [Fact]
    public async Task FlushAsync_WithoutPendingWork_CompletesImmediately()
    {
        GatedStateStore store = new();
        await using SessionStateWriter writer = new(store, NullLogger.Instance);
        writer.Start();

        await writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Empty(store.Written);
    }

    [Fact]
    public async Task FailedWrite_DoesNotStopLaterWrites()
    {
        GatedStateStore store = new() { FailuresRemaining = 1 };
        await using SessionStateWriter writer = new(store, NullLogger.Instance);
        writer.Start();

        writer.Request(CreateSnapshot(1));

        using (CancellationTokenSource deadline = new(TimeSpan.FromMilliseconds(200)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => writer.FlushAsync(deadline.Token));
        }

        Assert.Empty(store.Written);

        writer.Request(CreateSnapshot(2));
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        SessionSnapshot written = Assert.Single(store.Written);
        Assert.Equal(2, Marker(written));
    }

    [Fact]
    public async Task DisposeAsync_DrainsThePendingWrite()
    {
        GatedStateStore store = new();
        SessionStateWriter writer = new(store, NullLogger.Instance);
        writer.Start();

        writer.Request(CreateSnapshot(4));
        await writer.DisposeAsync();

        SessionSnapshot written = Assert.Single(store.Written);
        Assert.Equal(4, Marker(written));
    }

    [Fact]
    public async Task RequestsAfterDispose_AreIgnored()
    {
        GatedStateStore store = new();
        SessionStateWriter writer = new(store, NullLogger.Instance);

        await writer.DisposeAsync();
        writer.Request(CreateSnapshot(5));

        Assert.Empty(store.Written);
    }

    [Fact]
    public async Task Request_BeforeStart_IsWrittenOnceTheWriterStarts()
    {
        GatedStateStore store = new();
        await using SessionStateWriter writer = new(store, NullLogger.Instance);

        writer.Request(CreateSnapshot(7));

        // Construction only wires the queue: nothing is written until the
        // coordinator's initialize phase starts the loop.
        Assert.Empty(store.Written);
        Assert.Equal(0, writer.DurableVersion);

        writer.Start();
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        SessionSnapshot written = Assert.Single(store.Written);
        Assert.Equal(7, Marker(written));
    }

    private static SessionSnapshot CreateSnapshot(int marker) =>
        new(
            SessionLifecycleState.Running,
            [new PortSessionStatus(Guid.Empty, PortSessionState.Connected, RetryCount: marker)]);

    private static int Marker(SessionSnapshot snapshot) => snapshot.Ports[0].RetryCount;

    private sealed class GatedStateStore : ISessionStateStore
    {
        private readonly Lock _lock = new();
        private readonly List<SessionSnapshot> _written = [];
        private readonly TaskCompletionSource _firstSaveEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GatedStateStore(bool blockFirstSave = false)
        {
            if (!blockFirstSave)
            {
                _gate.TrySetResult();
            }
        }

        public Task FirstSaveEntered => _firstSaveEntered.Task;

        public int FailuresRemaining { get; set; }

        public IReadOnlyList<SessionSnapshot> Written
        {
            get
            {
                lock (_lock)
                {
                    return [.. _written];
                }
            }
        }

        public void ReleaseGate() => _gate.TrySetResult();

        public Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<SessionSnapshot?>(null);

        public async Task SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _firstSaveEntered.TrySetResult();
            await _gate.Task.ConfigureAwait(false);

            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("simulated write failure");
            }

            lock (_lock)
            {
                _written.Add(snapshot);
            }
        }
    }
}

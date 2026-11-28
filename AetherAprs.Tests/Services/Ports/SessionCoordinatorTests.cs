// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Data;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;

using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class SessionCoordinatorTests
{
    [Fact]
    public async Task StartAsync_RetriesWithBoundedJitteredBackoff_ThenRuns()
    {
        Guid portId = Guid.NewGuid();
        FakePortController controller = new() { FailuresBeforeSuccess = 2 };
        RecordingStateStore store = new();
        List<TimeSpan> delays = [];
        await using SessionCoordinator coordinator = CreateCoordinator(
            controller,
            store,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);
        await coordinator.FlushAsync(TestContext.Current.CancellationToken);

        PortSessionStatus status = Assert.Single(coordinator.Ports);
        Assert.Equal(SessionLifecycleState.Running, coordinator.State);
        Assert.Equal(PortSessionState.Connected, status.State);
        Assert.Equal(3, controller.ConnectAttempts);
        Assert.Equal(2, delays.Count);
        Assert.InRange(delays[0], TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1));
        Assert.InRange(delays[1], TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Assert.Equal(PortSessionState.Connected, Assert.Single(store.LastSnapshot!.Ports).State);
    }

    [Fact]
    public async Task ReportReceiveAndTransmit_PersistUtcObservations()
    {
        Guid portId = Guid.NewGuid();
        FakePortController controller = new();
        RecordingStateStore store = new();
        await using SessionCoordinator coordinator = CreateCoordinator(controller, store);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);
        await coordinator.ReportReceiveAsync(portId, TestContext.Current.CancellationToken);
        await coordinator.ReportTransmitAsync(portId, TestContext.Current.CancellationToken);

        PortSessionStatus status = Assert.Single(coordinator.Ports);
        Assert.NotNull(status.LastReceiveUtc);
        Assert.NotNull(status.LastTransmitUtc);
        Assert.Equal(DateTimeKind.Utc, status.LastReceiveUtc!.Value.UtcDateTime.Kind);
        Assert.Equal(DateTimeKind.Utc, status.LastTransmitUtc!.Value.UtcDateTime.Kind);
    }

    [Fact]
    public async Task StopAsync_CancelsPendingConnect_AndDisconnectsPort()
    {
        Guid portId = Guid.NewGuid();
        FakePortController controller = new() { BlockConnect = true };
        RecordingStateStore store = new();
        await using SessionCoordinator coordinator = CreateCoordinator(controller, store);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);
        await controller.ConnectStarted.Task;

        await coordinator.StopAsync();

        Assert.Equal(SessionLifecycleState.Stopped, coordinator.State);
        Assert.Equal(PortSessionState.Stopped, Assert.Single(coordinator.Ports).State);
        Assert.Equal(1, controller.DisconnectAttempts);
    }

    [Fact]
    public async Task StopAsync_UsesAnInternalDeadlineWhenATransportHangs()
    {
        Guid portId = Guid.NewGuid();
        FakePortController controller = new() { BlockDisconnect = true };
        await using SessionCoordinator coordinator = CreateCoordinator(
            controller,
            new RecordingStateStore(),
            teardownTimeout: TimeSpan.FromMilliseconds(100));

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);

        Stopwatch stopwatch = Stopwatch.StartNew();
        await coordinator.StopAsync();
        stopwatch.Stop();

        Assert.Equal(SessionLifecycleState.Stopped, coordinator.State);
        Assert.Equal(PortSessionState.Stopped, Assert.Single(coordinator.Ports).State);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(80), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Intent_IsSeparateFromRuntimeStatus_AndDisarmsOnStop()
    {
        Guid portId = Guid.NewGuid();
        await using SessionCoordinator coordinator = CreateCoordinator(
            new FakePortController(),
            new RecordingStateStore());

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.False(coordinator.Intent.IsArmed);

        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);

        Assert.True(coordinator.Intent.IsArmed);
        Assert.True(coordinator.Intent.Includes(portId));
        Assert.Equal(portId, Assert.Single(coordinator.Intent.PortIds));

        await coordinator.StopAsync();

        Assert.False(coordinator.Intent.IsArmed);
        Assert.Empty(coordinator.Intent.PortIds);
    }

    [Fact]
    public async Task PeriodicDiagnosticFlush_PersistsTheLatestTimestamps()
    {
        Guid portId = Guid.NewGuid();
        RecordingStateStore store = new();
        TestClock clock = new();
        await using SessionCoordinator coordinator = CreateCoordinator(
            new FakePortController(),
            store,
            timeProvider: clock,
            diagnosticPersistInterval: TimeSpan.FromMinutes(1));

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);
        await coordinator.FlushAsync(TestContext.Current.CancellationToken);
        int savesAfterStart = store.SaveCalls;

        await coordinator.ReportReceiveAsync(portId, TestContext.Current.CancellationToken);
        await coordinator.FlushAsync(TestContext.Current.CancellationToken);

        // A receive timestamp is diagnostic state: the transition path must not rewrite
        // the session file for it.
        Assert.Equal(savesAfterStart, store.SaveCalls);

        clock.Advance(TimeSpan.FromMinutes(1));
        await coordinator.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(savesAfterStart + 1, store.SaveCalls);
        Assert.NotNull(Assert.Single(store.LastSnapshot!.Ports).LastReceiveUtc);
    }

    [Fact]
    public async Task ReportPortFaultAsync_StartsReconnectLoop()
    {
        Guid portId = Guid.NewGuid();
        FakePortController controller = new();
        RecordingStateStore store = new();
        await using SessionCoordinator coordinator = CreateCoordinator(controller, store);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);
        Assert.Equal(1, controller.ConnectAttempts);

        await coordinator.ReportPortFaultAsync(
            portId,
            new InvalidOperationException("link lost"),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, controller.ConnectAttempts);
        Assert.Equal(SessionLifecycleState.Running, coordinator.State);
        Assert.Equal(PortSessionState.Connected, Assert.Single(coordinator.Ports).State);
    }

    [Fact]
    public async Task InitializeAsync_RestoresPortsAsStopped()
    {
        Guid portId = Guid.NewGuid();
        RecordingStateStore store = new()
        {
            Snapshot = new SessionSnapshot(
                SessionLifecycleState.Running,
                [new PortSessionStatus(portId, PortSessionState.Connected, RetryCount: 4)])
        };
        await using SessionCoordinator coordinator = CreateCoordinator(new FakePortController(), store);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SessionLifecycleState.Stopped, coordinator.State);
        PortSessionStatus status = Assert.Single(coordinator.Ports);
        Assert.Equal(PortSessionState.Stopped, status.State);
        Assert.Equal(4, status.RetryCount);
    }

    [Fact]
    public async Task ProcessRestart_RestoresPortStopped_ThenResumesOnExplicitStart()
    {
        Guid portId = Guid.NewGuid();
        RecordingStateStore store = new()
        {
            Snapshot = new SessionSnapshot(
                SessionLifecycleState.Running,
                [new PortSessionStatus(portId, PortSessionState.Connected)])
        };
        FakePortController controller = new();
        await using SessionCoordinator coordinator = CreateCoordinator(controller, store);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SessionLifecycleState.Stopped, coordinator.State);
        Assert.Equal(PortSessionState.Stopped, Assert.Single(coordinator.Ports).State);
        Assert.Equal(0, controller.ConnectAttempts);

        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);

        Assert.Equal(SessionLifecycleState.Running, coordinator.State);
        Assert.Equal(PortSessionState.Connected, Assert.Single(coordinator.Ports).State);
        Assert.Equal(1, controller.ConnectAttempts);
    }

    [Fact]
    public async Task SessionStateStore_RoundTripsSnapshotThroughTheDatabase()
    {
        using TempAppDatabase database = TempAppDatabase.CreateEmpty();
        using AppDbContext context = database.CreateContext();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        ISessionStateStore store = StoreFactory.CreateSessionStateStore(database);

        Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));

        Guid portId = Guid.NewGuid();
        SessionSnapshot snapshot = new(
            SessionLifecycleState.Degraded,
            [
                new PortSessionStatus(
                    portId,
                    PortSessionState.Degraded,
                    LastReceiveUtc: new DateTimeOffset(2026, 11, 27, 8, 30, 0, TimeSpan.Zero),
                    RetryCount: 3,
                    NextRetryUtc: new DateTimeOffset(2026, 11, 27, 8, 31, 0, TimeSpan.Zero),
                    Error: "modem busy")
            ]);

        await store.SaveAsync(snapshot, TestContext.Current.CancellationToken);
        SessionSnapshot? loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.State, loaded.State);
        PortSessionStatus status = Assert.Single(loaded.Ports);
        Assert.Equal(snapshot.Ports[0], status);

        // The snapshot is replaced as a whole: a port that disappears from the
        // snapshot must not linger in the persisted rows.
        await store.SaveAsync(snapshot with { Ports = [] }, TestContext.Current.CancellationToken);
        SessionSnapshot? replaced = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(replaced);
        Assert.Empty(replaced.Ports);
    }

    private static SessionCoordinator CreateCoordinator(
        FakePortController controller,
        RecordingStateStore store,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? teardownTimeout = null,
        TimeSpan? diagnosticPersistInterval = null,
        TimeProvider? timeProvider = null) => new(
            controller,
            store,
            timeProvider: timeProvider,
            delayAsync: delay ?? ((_, _) => Task.CompletedTask),
            jitter: () => 0,
            initialBackoff: TimeSpan.FromSeconds(1),
            maximumBackoff: TimeSpan.FromSeconds(2),
            teardownTimeout: teardownTimeout,
            diagnosticPersistInterval: diagnosticPersistInterval ?? TimeSpan.Zero);

    private sealed class FakePortController : ISessionPortController
    {
        public int FailuresBeforeSuccess { get; set; }
        public bool BlockConnect { get; set; }
        public bool BlockDisconnect { get; set; }
        public int ConnectAttempts { get; private set; }
        public int DisconnectAttempts { get; private set; }
        public TaskCompletionSource ConnectStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ConnectAsync(Guid portId, CancellationToken cancellationToken)
        {
            ConnectAttempts++;
            ConnectStarted.TrySetResult();

            if (BlockConnect)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (ConnectAttempts <= FailuresBeforeSuccess)
            {
                throw new InvalidOperationException("simulated transport failure");
            }
        }

        public async Task DisconnectAsync(Guid portId, CancellationToken cancellationToken)
        {
            DisconnectAttempts++;

            if (BlockDisconnect)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }
    }

    private sealed class RecordingStateStore : ISessionStateStore
    {
        public SessionSnapshot? Snapshot { get; set; }
        public SessionSnapshot? LastSnapshot { get; private set; }
        public int SaveCalls { get; private set; }

        public Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot);

        public Task SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            SaveCalls++;
            LastSnapshot = snapshot;
            return Task.CompletedTask;
        }
    }
}

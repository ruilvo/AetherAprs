// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Services.Ports;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class SessionCoordinatorTests
{
    [Fact]
    public async Task StartAsync_RetriesWithBoundedJitteredBackoff_ThenRuns()
    {
        var portId = Guid.NewGuid();
        var controller = new FakePortController { FailuresBeforeSuccess = 2 };
        var store = new RecordingStateStore();
        var delays = new List<TimeSpan>();
        await using var coordinator = CreateCoordinator(
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

        var status = Assert.Single(coordinator.Ports);
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
        var portId = Guid.NewGuid();
        var controller = new FakePortController();
        var store = new RecordingStateStore();
        await using var coordinator = CreateCoordinator(controller, store);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);
        await coordinator.ReportReceiveAsync(portId, TestContext.Current.CancellationToken);
        await coordinator.ReportTransmitAsync(portId, TestContext.Current.CancellationToken);

        var status = Assert.Single(coordinator.Ports);
        Assert.NotNull(status.LastReceiveUtc);
        Assert.NotNull(status.LastTransmitUtc);
        Assert.Equal(DateTimeKind.Utc, status.LastReceiveUtc!.Value.UtcDateTime.Kind);
        Assert.Equal(DateTimeKind.Utc, status.LastTransmitUtc!.Value.UtcDateTime.Kind);
    }

    [Fact]
    public async Task StopAsync_CancelsPendingConnect_AndDisconnectsPort()
    {
        var portId = Guid.NewGuid();
        var controller = new FakePortController { BlockConnect = true };
        var store = new RecordingStateStore();
        await using var coordinator = CreateCoordinator(controller, store);

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
        var portId = Guid.NewGuid();
        var controller = new FakePortController { BlockDisconnect = true };
        await using var coordinator = CreateCoordinator(
            controller,
            new RecordingStateStore(),
            teardownTimeout: TimeSpan.FromMilliseconds(100));

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        await coordinator.StopAsync();
        stopwatch.Stop();

        Assert.Equal(SessionLifecycleState.Stopped, coordinator.State);
        Assert.Equal(PortSessionState.Stopped, Assert.Single(coordinator.Ports).State);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(80), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Intent_IsSeparateFromRuntimeStatus_AndDisarmsOnStop()
    {
        var portId = Guid.NewGuid();
        await using var coordinator = CreateCoordinator(
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
        var portId = Guid.NewGuid();
        var store = new RecordingStateStore();
        await using var coordinator = CreateCoordinator(
            new FakePortController(),
            store,
            diagnosticPersistInterval: TimeSpan.FromMilliseconds(50));

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);
        await coordinator.FlushAsync(TestContext.Current.CancellationToken);
        var savesAfterStart = store.SaveCalls;

        await coordinator.ReportReceiveAsync(portId, TestContext.Current.CancellationToken);

        await EventuallyAsync(
            () => store is { SaveCalls: > 0, LastSnapshot: { Ports: [var latest] } }
                && latest.LastReceiveUtc is not null);

        Assert.True(store.SaveCalls > savesAfterStart);
    }

    [Fact]
    public async Task ReportPortFaultAsync_StartsReconnectLoop()
    {
        var portId = Guid.NewGuid();
        var controller = new FakePortController();
        var store = new RecordingStateStore();
        await using var coordinator = CreateCoordinator(controller, store);

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
        var portId = Guid.NewGuid();
        var store = new RecordingStateStore
        {
            Snapshot = new SessionSnapshot(
                SessionLifecycleState.Running,
                [new PortSessionStatus(portId, PortSessionState.Connected, RetryCount: 4)])
        };
        await using var coordinator = CreateCoordinator(new FakePortController(), store);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SessionLifecycleState.Stopped, coordinator.State);
        var status = Assert.Single(coordinator.Ports);
        Assert.Equal(PortSessionState.Stopped, status.State);
        Assert.Equal(4, status.RetryCount);
    }

    [Fact]
    public async Task ProcessRestart_RestoresPortStopped_ThenResumesOnExplicitStart()
    {
        var portId = Guid.NewGuid();
        var store = new RecordingStateStore
        {
            Snapshot = new SessionSnapshot(
                SessionLifecycleState.Running,
                [new PortSessionStatus(portId, PortSessionState.Connected)])
        };
        var controller = new FakePortController();
        await using var coordinator = CreateCoordinator(controller, store);

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
    public async Task FileSessionStateStore_RoundTripsSnapshotAtomically()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"aetheraprs-session-{Guid.NewGuid():N}",
            "session.json");
        var snapshot = new SessionSnapshot(
            SessionLifecycleState.Degraded,
            [new PortSessionStatus(Guid.NewGuid(), PortSessionState.Degraded, RetryCount: 3)]);
        var store = new FileSessionStateStore(path);

        try
        {
            await store.SaveAsync(snapshot, TestContext.Current.CancellationToken);
            var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.NotNull(loaded);
            Assert.Equal(snapshot.State, loaded.State);
            Assert.Equal(snapshot.Ports.Count, loaded.Ports.Count);
            Assert.Equal(snapshot.Ports[0], loaded.Ports[0]);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static SessionCoordinator CreateCoordinator(
        FakePortController controller,
        RecordingStateStore store,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? teardownTimeout = null,
        TimeSpan? diagnosticPersistInterval = null)
    {
        return new SessionCoordinator(
            controller,
            store,
            delayAsync: delay ?? ((_, _) => Task.CompletedTask),
            jitter: () => 0,
            initialBackoff: TimeSpan.FromSeconds(1),
            maximumBackoff: TimeSpan.FromSeconds(2),
            teardownTimeout: teardownTimeout,
            diagnosticPersistInterval: diagnosticPersistInterval ?? TimeSpan.Zero);
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), "The expected session telemetry state was not observed.");
    }

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

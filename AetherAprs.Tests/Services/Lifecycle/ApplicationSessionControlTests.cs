// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Models;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Lifecycle;
using AetherAprs.Services.Location;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.Services.Lifecycle;

public sealed class ApplicationSessionControlTests
{
    [Fact]
    public async Task StopAsync_DisarmsIntent_AndALateFaultCannotReconnect()
    {
        Guid portId = Guid.NewGuid();
        FakePortController controller = new() { BlockConnect = true };
        await using SessionCoordinator coordinator = CreateCoordinator(controller);
        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);
        await controller.ConnectStarted.Task;
        ApplicationSessionControl control = CreateControl(coordinator);

        ApplicationSessionStopResult result = await control.StopAsync();

        Assert.True(result.Succeeded);
        Assert.False(coordinator.Intent.IsArmed);
        Assert.Equal(SessionLifecycleState.Stopped, coordinator.State);
        Assert.Equal(1, controller.DisconnectAttempts);

        // A transport callback that was already in flight when the stop ran must
        // not reopen the session or move the lifecycle back to a running state.
        await coordinator.ReportPortFaultAsync(
            portId,
            new InvalidOperationException("late transport callback"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, controller.ConnectAttempts);
        Assert.Equal(SessionLifecycleState.Stopped, coordinator.State);
        Assert.Equal(PortSessionState.Stopped, Assert.Single(coordinator.Ports).State);
    }

    [Fact]
    public async Task StopAsync_SweepsSessionsTheCoordinatorDidNotArm()
    {
        // A send can start a modem implicitly, so the coordinator's armed set is not
        // the whole running set: the port service sweep is what closes that gap.
        FakePortService portService = new();
        await using SessionCoordinator coordinator = CreateCoordinator(new FakePortController());
        ApplicationSessionControl control = CreateControl(coordinator, portService: portService);

        ApplicationSessionStopResult result = await control.StopAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, portService.StopAllCalls);
    }

    [Fact]
    public async Task StopAsync_CancelsLocationWork_AndReleasesEveryKeepAliveReason()
    {
        FakeLocationTrackingService location = new();
        FakeKeepAliveService keepAlive = new();
        await using SessionCoordinator coordinator = CreateCoordinator(new FakePortController());
        ApplicationSessionControl control = CreateControl(coordinator, location: location, keepAlive: keepAlive);

        ApplicationSessionStopResult result = await control.StopAsync();

        Assert.True(result.Succeeded);
        Assert.True(location.StopCalled);
        Assert.Equal(
            new[] { KeepAliveReason.LocationTracking, KeepAliveReason.Ports },
            keepAlive.Released);
    }

    [Fact]
    public async Task StopAsync_ReportsAFailedStep_AndStillRunsTheRest()
    {
        FakePortService portService = new()
        {
            StopFailure = new InvalidOperationException("simulated failure")
        };
        FakeLocationTrackingService location = new();
        FakeKeepAliveService keepAlive = new();
        await using SessionCoordinator coordinator = CreateCoordinator(new FakePortController());
        ApplicationSessionControl control = CreateControl(
            coordinator,
            portService: portService,
            location: location,
            keepAlive: keepAlive);

        ApplicationSessionStopResult result = await control.StopAsync();

        Assert.False(result.Succeeded);
        ApplicationSessionStopFailure failure = Assert.Single(result.Failures);
        Assert.Equal("ports", failure.Step);
        Assert.Equal("simulated failure", failure.Message);

        // The operator asked to close the application: one failed step must not
        // leave location polling or the foreground service running.
        Assert.True(location.StopCalled);
        Assert.Contains(KeepAliveReason.Ports, keepAlive.Released);
    }

    [Fact]
    public async Task StopAsync_SerializesConcurrentStops()
    {
        FakePortService portService = new() { BlockStop = true };
        await using SessionCoordinator coordinator = CreateCoordinator(new FakePortController());
        ApplicationSessionControl control = CreateControl(coordinator, portService: portService);

        Task<ApplicationSessionStopResult> first = control.StopAsync();
        await portService.StopEntered.Task;
        Task<ApplicationSessionStopResult> second = control.StopAsync();

        portService.ReleaseStop();

        await Task.WhenAll(first, second);

        Assert.Equal(1, portService.MaxConcurrentStops);
        Assert.Equal(2, portService.StopAllCalls);
    }

    [Fact]
    public async Task StopAsync_WhenNothingRuns_StillCompletesEveryStep()
    {
        await using SessionCoordinator coordinator = CreateCoordinator(new FakePortController());
        FakeLocationTrackingService location = new();
        FakeKeepAliveService keepAlive = new();
        ApplicationSessionControl control = CreateControl(coordinator, location: location, keepAlive: keepAlive);

        ApplicationSessionStopResult result = await control.StopAsync();

        Assert.True(result.Succeeded);
        Assert.False(coordinator.Intent.IsArmed);
        Assert.True(location.StopCalled);
        Assert.Equal(2, keepAlive.Released.Count);
    }

    private static ApplicationSessionControl CreateControl(
        SessionCoordinator coordinator,
        FakePortService? portService = null,
        FakeLocationTrackingService? location = null,
        FakeKeepAliveService? keepAlive = null) => new(
            coordinator,
            portService ?? new FakePortService(),
            new LocationCoordinator(
            location ?? new FakeLocationTrackingService(),
            TimeProvider.System,
            NullLogger<LocationCoordinator>.Instance),
            keepAlive ?? new FakeKeepAliveService(),
            NullLogger<ApplicationSessionControl>.Instance);

    private static SessionCoordinator CreateCoordinator(FakePortController controller) =>
        new(
            controller,
            new RecordingStateStore(),
            delayAsync: (_, _) => Task.CompletedTask,
            jitter: () => 0,
            initialBackoff: TimeSpan.FromSeconds(1),
            maximumBackoff: TimeSpan.FromSeconds(2),
            diagnosticPersistInterval: TimeSpan.Zero);

    private sealed class FakePortController : ISessionPortController
    {
        public bool BlockConnect { get; set; }
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
        }

        public Task DisconnectAsync(Guid portId, CancellationToken cancellationToken)
        {
            DisconnectAttempts++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStateStore : ISessionStateStore
    {
        public Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<SessionSnapshot?>(null);

        public Task SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakePortService : IPortService
    {
        private readonly TaskCompletionSource _stopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeStops;

        public int StopAllCalls { get; private set; }
        public int MaxConcurrentStops { get; private set; }
        public bool BlockStop { get; init; }
        public Exception? StopFailure { get; init; }
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<PortConfig> Ports => [];

        public event EventHandler? PortsChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived
        {
            add { }
            remove { }
        }

        public void ReleaseStop() => _stopGate.TrySetResult();

        public async Task StopAllPortsAsync()
        {
            StopAllCalls++;
            int active = Interlocked.Increment(ref _activeStops);
            MaxConcurrentStops = Math.Max(MaxConcurrentStops, active);
            StopEntered.TrySetResult();

            try
            {
                if (BlockStop)
                {
                    await _stopGate.Task;
                }

                if (StopFailure is not null)
                {
                    throw StopFailure;
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeStops);
            }
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddPortAsync(PortConfig port) => Task.CompletedTask;
        public Task UpdatePortAsync(PortConfig port) => Task.CompletedTask;
        public Task RemovePortAsync(Guid id) => Task.CompletedTask;
        public Task SetPortEnabledAsync(Guid id, bool enabled) => Task.CompletedTask;
        public Task SetPortShowOnMapAsync(Guid id, bool showOnMap) => Task.CompletedTask;
        public Task SendPacketAsync(Guid id, AprsPacket packet) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeLocationTrackingService : ILocationTrackingService
    {
        public bool StopCalled { get; private set; }

        public LocationData? CurrentLocation => null;
        public bool IsTracking => false;
        public bool IsLocationAvailable => false;

        public event EventHandler<LocationData>? LocationUpdated
        {
            add { }
            remove { }
        }

        public Task StartTrackingAsync() => Task.CompletedTask;
        public Task RequestPermissionAndStartTrackingAsync() => Task.CompletedTask;

        public void StopTracking() => StopCalled = true;

        public void Dispose()
        {
        }
    }

    private sealed class FakeKeepAliveService : IKeepAliveService
    {
        public List<KeepAliveReason> Released { get; } = [];

        public KeepAliveStatus Status { get; private set; } = KeepAliveStatus.Stopped(ForegroundServiceType.None);

        public event EventHandler<KeepAliveStatusChangedEventArgs>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task<KeepAliveStatus> AcquireAsync(KeepAliveReason reason) => Task.FromResult(Status);

        public Task<KeepAliveStatus> ReleaseAsync(KeepAliveReason reason)
        {
            Released.Add(reason);
            return Task.FromResult(Status);
        }
    }
}

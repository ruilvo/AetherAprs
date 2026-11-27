// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using AetherAprs.Transports.Kiss;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class SessionCoordinatorTelemetryTests
{
    [Fact]
    public async Task TransportTelemetry_UpdatesStatus_ReconnectsAndNotifiesObservers()
    {
        Guid portId = Guid.NewGuid();
        RecordingPortController controller = new();
        RecordingStateStore store = new();
        FakePortService portService = new();
        ConcurrentQueue<SessionSnapshot> states = new();
        ConcurrentQueue<TimeSpan> delays = new();

        await using SessionCoordinator coordinator = new(
            controller,
            store,
            delayAsync: (delay, _) =>
            {
                delays.Enqueue(delay);
                return Task.CompletedTask;
            },
            jitter: () => 0,
            initialBackoff: TimeSpan.FromSeconds(1),
            maximumBackoff: TimeSpan.FromSeconds(3));
        await using SessionCoordinatorTelemetryBridge bridge = new(
            portService,
            coordinator,
            NullLogger<SessionCoordinatorTelemetryBridge>.Instance);
        coordinator.StateChanged += (_, args) => states.Enqueue(args.Snapshot);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);

        portService.RaisePacketReceived(new PortPacketReceivedEventArgs
        {
            PortId = portId,
            Packet = null!
        });
        portService.RaisePacketTransmitted(portId, null!);
        await bridge.FlushAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<PortSessionStatus> ports = coordinator.Ports;
        PortSessionStatus observed = Assert.Single(ports);
        Assert.NotNull(observed.LastReceiveUtc);
        Assert.NotNull(observed.LastTransmitUtc);

        portService.RaiseTransportFault(portId, new InvalidOperationException("transport lost"));
        await bridge.FlushAsync(TestContext.Current.CancellationToken);
        await coordinator.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, controller.ConnectAttempts);

        PortSessionStatus finalStatus = Assert.Single(coordinator.Ports);
        Assert.Equal(PortSessionState.Connected, finalStatus.State);
        Assert.Equal(SessionLifecycleState.Running, coordinator.State);
        Assert.Contains(states, snapshot =>
            snapshot.State == SessionLifecycleState.Degraded &&
            snapshot.Ports[0].State == PortSessionState.Degraded);
        Assert.Contains(states, snapshot =>
            snapshot.State == SessionLifecycleState.Running &&
            snapshot.Ports[0].State == PortSessionState.Connected);
        Assert.All(delays, delay =>
            Assert.InRange(delay, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(3)));
        Assert.Equal(SessionLifecycleState.Running, store.LastSnapshot!.State);
    }

    [Fact]
    public async Task RepeatedPacketsWithoutStateChange_DoNotRewriteSessionState()
    {
        Guid portId = Guid.NewGuid();
        RecordingPortController controller = new();
        RecordingStateStore store = new();
        FakePortService portService = new();
        TestClock clock = new();

        await using SessionCoordinator coordinator = new(
            controller,
            store,
            timeProvider: clock,
            diagnosticPersistInterval: TimeSpan.FromMinutes(1));
        await using SessionCoordinatorTelemetryBridge bridge = new(
            portService,
            coordinator,
            NullLogger<SessionCoordinatorTelemetryBridge>.Instance);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);
        await coordinator.FlushAsync(TestContext.Current.CancellationToken);
        int savesAfterTransition = store.SaveCalls;

        // A busy feed produces thousands of packets; none of them may trigger a
        // full session-state rewrite, a UI event or a diagnostic.
        for (int i = 0; i < 200; i++)
        {
            portService.RaisePacketReceived(new PortPacketReceivedEventArgs
            {
                PortId = portId,
                Packet = null!
            });
        }

        await bridge.FlushAsync(TestContext.Current.CancellationToken);
        await coordinator.FlushAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(Assert.Single(coordinator.Ports).LastReceiveUtc);
        Assert.Equal(savesAfterTransition, store.SaveCalls);

        // The periodic diagnostic flush is the only writer of a pure timestamp change.
        clock.Advance(TimeSpan.FromMinutes(1));
        await coordinator.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(savesAfterTransition + 1, store.SaveCalls);
        Assert.NotNull(Assert.Single(store.LastSnapshot!.Ports).LastReceiveUtc);
    }

    private sealed class RecordingPortController : ISessionPortController
    {
        public int ConnectAttempts { get; private set; }

        public Task ConnectAsync(Guid portId, CancellationToken cancellationToken)
        {
            ConnectAttempts++;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(Guid portId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingStateStore : ISessionStateStore
    {
        public SessionSnapshot? LastSnapshot { get; private set; }

        public int SaveCalls { get; private set; }

        public Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<SessionSnapshot?>(null);

        public Task SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            SaveCalls++;
            LastSnapshot = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePortService : IPortService, IPortServiceTelemetry
    {
        public IReadOnlyList<PortConfig> Ports { get; } = [];

        public event EventHandler? PortsChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;

        public event EventHandler<PortPacketTransmittedEventArgs>? PacketTransmitted;

        public event EventHandler<PortTransportFaultEventArgs>? TransportFaulted;

        public event EventHandler<PortConnectionProgressEventArgs>? ConnectionProgress
        {
            add { }
            remove { }
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddPortAsync(PortConfig port) => Task.CompletedTask;

        public Task UpdatePortAsync(PortConfig port) => Task.CompletedTask;

        public Task RemovePortAsync(Guid id) => Task.CompletedTask;

        public Task SetPortEnabledAsync(Guid id, bool enabled) => Task.CompletedTask;

        public Task SetPortShowOnMapAsync(Guid id, bool showOnMap) => Task.CompletedTask;

        public Task SendPacketAsync(Guid id, AprsPacket packet) => Task.CompletedTask;

        public Task StopAllPortsAsync() => Task.CompletedTask;

        public long GetDroppedPacketCount(Guid portId) => 0;

        public long GetDroppedForwardCount(Guid portId) => 0;

        public KissConnectionProgress? GetConnectionProgress(Guid portId) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaisePacketReceived(PortPacketReceivedEventArgs args) =>
            PacketReceived?.Invoke(this, args);

        public void RaisePacketTransmitted(Guid portId, AprsPacket packet) =>
            PacketTransmitted?.Invoke(this, new PortPacketTransmittedEventArgs(portId, packet));

        public void RaiseTransportFault(Guid portId, Exception exception) =>
            TransportFaulted?.Invoke(this, new PortTransportFaultEventArgs(portId, exception));
    }
}

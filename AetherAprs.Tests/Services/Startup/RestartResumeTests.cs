// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration;
using AetherAprs.Services.Ports;
using AetherAprs.Services.Startup;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.Services.Startup;

/// <summary>
/// The process-death harness: a restarted process must never continue the previous
/// process's runtime state, and must re-arm exactly the ports that are enabled now.
/// </summary>
public sealed class RestartResumeTests
{
    [Fact]
    public async Task Restart_RestoresNothingLiveBeforeTheArm()
    {
        PortConfig port = CreatePort("KISS", enabled: true);
        InMemorySessionStateStore store = new(new SessionSnapshot(
            SessionLifecycleState.Running,
            [new PortSessionStatus(port.Id, PortSessionState.Connected)]));

        await using SessionCoordinator coordinator = CreateCoordinator(store);
        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        // The previous process left a connected session on disk. The new process
        // starts from that record but with no live session and no intent.
        Assert.Equal(SessionLifecycleState.Stopped, coordinator.State);
        Assert.False(coordinator.Intent.IsArmed);
        Assert.All(coordinator.Ports, status => Assert.Equal(PortSessionState.Stopped, status.State));
    }

    [Fact]
    public async Task Restart_ArmsOnlyThePortsThatAreEnabledNow()
    {
        PortConfig stillEnabled = CreatePort("Still enabled", enabled: true);
        PortConfig disabledSince = CreatePort("Previously running, now disabled", enabled: false);
        InMemorySessionStateStore store = new(new SessionSnapshot(
            SessionLifecycleState.Running,
            [
                new PortSessionStatus(stillEnabled.Id, PortSessionState.Connected),
                new PortSessionStatus(disabledSince.Id, PortSessionState.Connected)
            ]));

        ISessionPortController controller = Substitute.For<ISessionPortController>();
        controller.ConnectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await using SessionCoordinator coordinator = CreateCoordinator(store, controller);
        IPortService portService = CreatePortService(stillEnabled, disabledSince);
        await using SessionCoordinatorTelemetryBridge bridge = new(
            portService,
            coordinator,
            NullLogger<SessionCoordinatorTelemetryBridge>.Instance);
        SessionStartup startup = new(
            portService,
            coordinator,
            bridge,
            NullLogger<SessionStartup>.Instance);

        await startup.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(coordinator.Intent.Includes(stillEnabled.Id));
        Assert.False(coordinator.Intent.Includes(disabledSince.Id));

        await EventuallyAsync(() => coordinator.Ports.Any(status =>
            status.PortId == stillEnabled.Id && status.State == PortSessionState.Connected));

        await controller.Received(1).ConnectAsync(stillEnabled.Id, Arg.Any<CancellationToken>());
        await controller.DidNotReceive().ConnectAsync(disabledSince.Id, Arg.Any<CancellationToken>());
    }

    private static SessionCoordinator CreateCoordinator(
        ISessionStateStore store,
        ISessionPortController? controller = null)
    {
        if (controller is null)
        {
            controller = Substitute.For<ISessionPortController>();
            controller.ConnectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
        }

        return new SessionCoordinator(
            controller,
            store,
            logger: NullLogger<SessionCoordinator>.Instance,
            diagnosticPersistInterval: TimeSpan.Zero);
    }

    private static IPortService CreatePortService(params PortConfig[] ports)
    {
        IPortService portService = Substitute.For<IPortService, IPortServiceTelemetry>();
        ((IPortService)portService).Ports.Returns(ports);
        return portService;
    }

    private static PortConfig CreatePort(string name, bool enabled) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsEnabled = enabled,
            TypeSettings = new KissSettings { Transport = new TcpKissTransportSettings() }
        };

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met before the deadline.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class InMemorySessionStateStore(SessionSnapshot? snapshot) : ISessionStateStore
    {
        public SessionSnapshot? Snapshot { get; private set; } = snapshot;

        public Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot);

        public Task SaveAsync(SessionSnapshot snapshotToSave, CancellationToken cancellationToken)
        {
            Snapshot = snapshotToSave;
            return Task.CompletedTask;
        }
    }
}

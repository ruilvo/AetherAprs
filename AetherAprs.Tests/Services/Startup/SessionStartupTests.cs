// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration;
using AetherAprs.Services.Ports;
using AetherAprs.Services.Startup;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.Services.Startup;

public sealed class SessionStartupTests
{
    [Fact]
    public async Task StartAsync_ArmsEveryEnabledPort()
    {
        PortConfig enabled = CreatePort("Enabled", enabled: true);
        PortConfig disabled = CreatePort("Disabled", enabled: false);
        IPortService portService = CreatePortService(enabled, disabled);

        await using SessionCoordinator coordinator = CreateCoordinator();
        await using SessionCoordinatorTelemetryBridge bridge = CreateBridge(portService, coordinator);
        SessionStartup startup = new(
            portService,
            coordinator,
            bridge,
            NullLogger<SessionStartup>.Instance);

        await startup.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(coordinator.Intent.IsArmed);
        Assert.True(coordinator.Intent.Includes(enabled.Id));
        Assert.False(coordinator.Intent.Includes(disabled.Id));
    }

    [Fact]
    public async Task StartAsync_WithoutEnabledPorts_LeavesTheSessionDisarmed()
    {
        IPortService portService = CreatePortService(CreatePort("Disabled", enabled: false));

        await using SessionCoordinator coordinator = CreateCoordinator();
        await using SessionCoordinatorTelemetryBridge bridge = CreateBridge(portService, coordinator);
        SessionStartup startup = new(
            portService,
            coordinator,
            bridge,
            NullLogger<SessionStartup>.Instance);

        await startup.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(coordinator.Intent.IsArmed);
        Assert.Equal(SessionLifecycleState.Stopped, coordinator.State);
    }

    private static SessionCoordinator CreateCoordinator()
    {
        ISessionPortController controller = Substitute.For<ISessionPortController>();
        controller.ConnectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        ISessionStateStore store = Substitute.For<ISessionStateStore>();
        store.LoadAsync(Arg.Any<CancellationToken>())
            .Returns((SessionSnapshot?)null);

        return new SessionCoordinator(
            controller,
            store,
            logger: NullLogger<SessionCoordinator>.Instance,
            diagnosticPersistInterval: TimeSpan.Zero);
    }

    private static SessionCoordinatorTelemetryBridge CreateBridge(
        IPortService portService,
        SessionCoordinator coordinator) =>
        new(portService, coordinator, NullLogger<SessionCoordinatorTelemetryBridge>.Instance);

    private static IPortService CreatePortService(params PortConfig[] ports)
    {
        // The telemetry bridge subscribes to transport events, so the double must
        // implement both interfaces the production PortService exposes.
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
}

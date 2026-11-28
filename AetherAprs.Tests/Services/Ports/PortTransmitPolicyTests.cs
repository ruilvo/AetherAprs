// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Ports;

using Geo;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class PortTransmitPolicyTests
{
    [Fact]
    public async Task SendAsync_RunningSession_SendsWithoutConsultingEnablement()
    {
        PortConfig port = CreatePort(isEnabled: false);
        IPortSessionManager sessionManager = Substitute.For<IPortSessionManager>();
        sessionManager
            .SendAsync(port.Id, Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>())
            .Returns(PortSessionSendStatus.Sent);

        PortTransmitPolicy policy = new(new StubConfigurationSource(port), sessionManager);

        PortTransmitOutcome outcome = await policy.SendAsync(
            port.Id,
            CreatePacket(),
            allowImplicitStart: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionSendStatus.Sent, outcome.Status);
        Assert.False(outcome.StartedSession);
        await sessionManager.DidNotReceive().StartAsync(Arg.Any<PortConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_NoSessionAndStartNotAllowed_ReportsNotRunningWithoutStarting()
    {
        PortConfig port = CreatePort(isEnabled: true);
        IPortSessionManager sessionManager = Substitute.For<IPortSessionManager>();
        sessionManager
            .SendAsync(port.Id, Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>())
            .Returns(PortSessionSendStatus.NotRunning);

        PortTransmitPolicy policy = new(new StubConfigurationSource(port), sessionManager);

        PortTransmitOutcome outcome = await policy.SendAsync(
            port.Id,
            CreatePacket(),
            allowImplicitStart: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionSendStatus.NotRunning, outcome.Status);
        Assert.False(outcome.StartedSession);
        await sessionManager.DidNotReceive().StartAsync(Arg.Any<PortConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_NoSessionAndDisabledPort_DoesNotStartAVetoedPort()
    {
        PortConfig port = CreatePort(isEnabled: false);
        IPortSessionManager sessionManager = Substitute.For<IPortSessionManager>();
        sessionManager
            .SendAsync(port.Id, Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>())
            .Returns(PortSessionSendStatus.NotRunning);

        PortTransmitPolicy policy = new(new StubConfigurationSource(port), sessionManager);

        PortTransmitOutcome outcome = await policy.SendAsync(
            port.Id,
            CreatePacket(),
            allowImplicitStart: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionSendStatus.NotRunning, outcome.Status);
        await sessionManager.DidNotReceive().StartAsync(Arg.Any<PortConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_NoSessionAndEnabledPort_StartsThenSendsAndReportsTheStart()
    {
        PortConfig port = CreatePort(isEnabled: true);
        IPortSessionManager sessionManager = Substitute.For<IPortSessionManager>();
        sessionManager
            .SendAsync(port.Id, Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>())
            .Returns(PortSessionSendStatus.NotRunning, PortSessionSendStatus.Sent);
        sessionManager
            .StartAsync(Arg.Any<PortConfig>(), Arg.Any<CancellationToken>())
            .Returns(PortSessionStartResult.Started(port.Id, "TcpKiss"));

        PortTransmitPolicy policy = new(new StubConfigurationSource(port), sessionManager);

        PortTransmitOutcome outcome = await policy.SendAsync(
            port.Id,
            CreatePacket(),
            allowImplicitStart: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionSendStatus.Sent, outcome.Status);
        Assert.True(outcome.StartedSession);
        await sessionManager.Received(1).StartAsync(Arg.Any<PortConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_FailedStart_ReportsNotRunningAndCarriesTheError()
    {
        PortConfig port = CreatePort(isEnabled: true);
        IPortSessionManager sessionManager = Substitute.For<IPortSessionManager>();
        sessionManager
            .SendAsync(port.Id, Arg.Any<AprsPacket>(), Arg.Any<CancellationToken>())
            .Returns(PortSessionSendStatus.NotRunning);
        InvalidOperationException failure = new("modem refused");
        sessionManager
            .StartAsync(Arg.Any<PortConfig>(), Arg.Any<CancellationToken>())
            .Returns(PortSessionStartResult.Failed(port.Id, failure));

        PortTransmitPolicy policy = new(new StubConfigurationSource(port), sessionManager);

        PortTransmitOutcome outcome = await policy.SendAsync(
            port.Id,
            CreatePacket(),
            allowImplicitStart: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(PortSessionSendStatus.NotRunning, outcome.Status);
        Assert.False(outcome.StartedSession);
        Assert.Same(failure, outcome.Error);
    }

    private static PortConfig CreatePort(bool isEnabled) => new()
    {
        Id = Guid.NewGuid(),
        Name = "KISS",
        IsEnabled = isEnabled,
        IsRx = true,
        IsTx = true,
        TypeSettings = new KissSettings
        {
            Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
        }
    };

    private static PositionPacket CreatePacket() => new()
    {
        Source = new Callsign("N0CALL", 1),
        Destination = new Callsign("APRS"),
        Location = new Coordinate(0, 0)
    };

    private sealed class StubConfigurationSource(params PortConfig[] ports) : IPortConfigurationSource
    {
        public IReadOnlyList<PortConfig> GetPorts() => ports;

        public PortConfig? Find(Guid id) => Array.Find(ports, port => port.Id == id);
    }
}

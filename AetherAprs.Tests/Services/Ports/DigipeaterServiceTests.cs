// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Configuration.Settings;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class DigipeaterServiceTests
{
    private static readonly Guid SourcePortId = Guid.NewGuid();
    private static readonly Guid RfPortId = Guid.NewGuid();
    private static readonly Guid AprsIsPortId = Guid.NewGuid();

    [Fact]
    public void GetDigipeatTargets_Disabled_ReturnsEmpty()
    {
        var service = CreateService(enableDigipeater: false);

        var targets = service.GetDigipeatTargets(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_OwnStationPacket_ReturnsEmpty()
    {
        var service = CreateService(callsignBase: "N0CALL");

        var targets = service.GetDigipeatTargets(
            CreatePositionPacket(source: "N0CALL"),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_TelemetryPacket_IsNotDigipeated()
    {
        var service = CreateService();

        var packet = new TelemetryPacket
        {
            Source = new Callsign("N5TEL"),
            Destination = new Callsign("APRS"),
            Path = [],
            Raw = "T#005,006,007,008,009,010,011",
            SequenceNumber = 5
        };

        var targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_CapabilitiesPacket_IsNotDigipeated()
    {
        var service = CreateService();

        var packet = new CapabilitiesPacket
        {
            Source = new Callsign("N5CAP"),
            Destination = new Callsign("APRS"),
            Path = [],
            Raw = "<STATIONCAPABILITIES>",
            Text = "STATIONCAPABILITIES"
        };

        var targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_DuplicateWithinRetentionWindow_IsSuppressed()
    {
        var service = CreateService();
        var packet = CreatePositionPacket();

        var first = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());
        var second = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());

        Assert.NotEmpty(first);
        Assert.Empty(second);
    }

    [Fact]
    public void GetDigipeatTargets_DifferentPacketFromSameSource_IsNotSuppressed()
    {
        var service = CreateService();
        var first = CreatePositionPacket();

        _ = service.GetDigipeatTargets(first, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());

        var second = CreatePositionPacket() with { Raw = "!4903.50N/07202.75W-Other comment" };
        var targets = service.GetDigipeatTargets(second, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());

        Assert.NotEmpty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_AprsIsToRfGateDisabled_ReturnsEmpty()
    {
        var service = CreateService(enableAprsIsToRfGate: false);

        var targets = service.GetDigipeatTargets(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_RfToAprsIs_ForwardsOriginalPacket()
    {
        var service = CreateService(callsignBase: "MYCALL", ssid: 9);

        var packet = CreatePositionPacket() with
        {
            Source = new Callsign("N5RF"),
            Path = [new Callsign("WIDE", 2)]
        };

        var targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts());

        var isTarget = Assert.Single(targets, t => t.PortId == AprsIsPortId);
        // RF→IS forwards the packet unchanged; APRS-IS handles distribution.
        Assert.Single(isTarget.Packet.Path);
        Assert.Equal("WIDE", isTarget.Packet.Path[0].Base);
        Assert.Equal(2, isTarget.Packet.Path[0].Ssid);
    }

    [Fact]
    public void GetDigipeatTargets_RfToRf_ForwardsWhenHopRemains()
    {
        var service = CreateService(callsignBase: "MYCALL", ssid: 9);

        var packet = CreatePositionPacket() with
        {
            Source = new Callsign("N5RF"),
            Path = [new Callsign("WIDE", 2)]
        };

        var targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts());

        var rfTarget = Assert.Single(targets, t => t.PortId == RfPortId);
        Assert.Equal(2, rfTarget.Packet.Path.Count);
        Assert.Equal("MYCALL", rfTarget.Packet.Path[0].Base);
        Assert.Equal(1, rfTarget.Packet.Path[1].Ssid);
    }

    [Fact]
    public void GetDigipeatTargets_PortWithoutTx_IsSkipped()
    {
        var service = CreateService();
        var ports = new List<PortInfo>
        {
            new(RfPortId, IsAprsIs: false, IsTxEnabled: false, AllowDigipeat: true)
        };

        var targets = service.GetDigipeatTargets(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            ports);

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_PortWithoutAllowDigipeat_IsSkipped()
    {
        var service = CreateService();
        var ports = new List<PortInfo>
        {
            new(RfPortId, IsAprsIs: false, IsTxEnabled: true, AllowDigipeat: false)
        };

        var targets = service.GetDigipeatTargets(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            ports);

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_SourcePort_IsExcluded()
    {
        var service = CreateService();
        var ports = new List<PortInfo>
        {
            new(SourcePortId, IsAprsIs: false, IsTxEnabled: true, AllowDigipeat: true)
        };

        var targets = service.GetDigipeatTargets(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: false,
            ports);

        Assert.Empty(targets);
    }

    private static PositionPacket CreatePositionPacket(string source = "N5SRC") => new()
    {
        Source = new Callsign(source),
        Destination = new Callsign("APRS"),
        Path = [],
        Raw = "!4903.50N/07202.75W-Test comment"
    };

    private static IReadOnlyList<PortInfo> CreatePorts() =>
    [
        new PortInfo(RfPortId, IsAprsIs: false, IsTxEnabled: true, AllowDigipeat: true),
        new PortInfo(AprsIsPortId, IsAprsIs: true, IsTxEnabled: true, AllowDigipeat: true)
    ];

    private static DigipeaterService CreateService(
        bool enableDigipeater = true,
        bool enableAprsIsToRfGate = true,
        string callsignBase = "N0CALL",
        byte ssid = 0)
    {
        var settings = new AppSettings();
        settings.Aprs.EnableDigipeater = enableDigipeater;
        settings.Aprs.EnableAprsIsToRfGate = enableAprsIsToRfGate;
        settings.Aprs.EnableRfToAprsIsGate = true;
        settings.Aprs.Callsign = callsignBase;
        settings.Aprs.Ssid = (AprsSsid)ssid;

        return new DigipeaterService(
            new TestConfigurationService(settings),
            NullLogger<DigipeaterService>.Instance);
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
        public TestConfigurationService(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; }

        public Task SaveSettingsAsync() => Task.CompletedTask;

        public AppSettings GetSettingsSnapshot() => Settings;
    }
}

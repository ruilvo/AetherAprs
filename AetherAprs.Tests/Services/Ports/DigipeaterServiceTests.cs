// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using AetherAprs.Configuration.Settings;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
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
        DigipeaterService service = CreateService(enableDigipeater: false);

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_OwnStationPacket_ReturnsEmpty()
    {
        DigipeaterService service = CreateService(callsignBase: "N0CALL");

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(
            CreatePositionPacket(source: "N0CALL"),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_TelemetryPacket_IsNotDigipeated()
    {
        DigipeaterService service = CreateService();

        TelemetryPacket packet = new()
        {
            Source = new Callsign("N5TEL"),
            Destination = new Callsign("APRS"),
            Path = [],
            Raw = "T#005,006,007,008,009,010,011",
            SequenceNumber = 5
        };

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_CapabilitiesPacket_IsNotDigipeated()
    {
        DigipeaterService service = CreateService();

        CapabilitiesPacket packet = new()
        {
            Source = new Callsign("N5CAP"),
            Destination = new Callsign("APRS"),
            Path = [],
            Raw = "<STATIONCAPABILITIES>",
            Text = "STATIONCAPABILITIES"
        };

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_DuplicateWithinRetentionWindow_IsSuppressed()
    {
        TestClock clock = new();
        DigipeaterService service = CreateService(timeProvider: clock);
        PositionPacket packet = CreatePositionPacket();

        IReadOnlyList<DigipeatTarget> first = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());
        clock.Advance(TimeSpan.FromSeconds(29));
        IReadOnlyList<DigipeatTarget> second = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());

        Assert.NotEmpty(first);
        Assert.Empty(second);
    }

    [Fact]
    public void GetDigipeatTargets_DuplicateAfterRetentionWindow_IsDigipeatedAgain()
    {
        TestClock clock = new();
        DigipeaterService service = CreateService(timeProvider: clock);
        PositionPacket packet = CreatePositionPacket();

        IReadOnlyList<DigipeatTarget> first = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());
        clock.Advance(TimeSpan.FromSeconds(31));
        IReadOnlyList<DigipeatTarget> again = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());

        Assert.NotEmpty(first);
        Assert.NotEmpty(again);
    }

    [Fact]
    public void GetDigipeatTargets_SamePacketOnDifferentPort_IsSuppressed()
    {
        DigipeaterService service = CreateService();
        PositionPacket packet = CreatePositionPacket();

        // Heard first on one RF port, then the identical packet arrives on a second
        // port. The shared heard table (source+dest+info hash) must suppress the
        // second digipeat so the packet is forwarded once, not once per port.
        IReadOnlyList<DigipeatTarget> first = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());
        IReadOnlyList<DigipeatTarget> second = service.GetDigipeatTargets(packet, RfPortId, sourcePortIsAprsIs: true, CreatePorts());

        Assert.NotEmpty(first);
        Assert.Empty(second);
    }

    [Fact]
    public void GetDigipeatTargets_SamePacketDifferentInfo_IsNotSuppressed()
    {
        DigipeaterService service = CreateService();
        PositionPacket first = CreatePositionPacket();

        _ = service.GetDigipeatTargets(first, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());

        // Same source and destination but different info field: must not be treated
        // as a duplicate by the source+dest+info hash.
        PositionPacket second = CreatePositionPacket() with { Raw = "!4903.50N/07202.75W-Different comment" };
        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(second, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());

        Assert.NotEmpty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_DifferentPacketFromSameSource_IsNotSuppressed()
    {
        DigipeaterService service = CreateService();
        PositionPacket first = CreatePositionPacket();

        _ = service.GetDigipeatTargets(first, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());

        PositionPacket second = CreatePositionPacket() with { Raw = "!4903.50N/07202.75W-Other comment" };
        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(second, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());

        Assert.NotEmpty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_AprsIsToRfGateDisabled_ReturnsEmpty()
    {
        DigipeaterService service = CreateService(enableAprsIsToRfGate: false);

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_RfToAprsIs_ForwardsOriginalPacket()
    {
        DigipeaterService service = CreateService(callsignBase: "MYCALL", ssid: 9);

        PositionPacket packet = CreatePositionPacket() with
        {
            Source = new Callsign("N5RF"),
            Path = [new Callsign("WIDE2", 2)]
        };

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts());

        DigipeatTarget isTarget = Assert.Single(targets, t => t.PortId == AprsIsPortId);
        // RF→IS forwards the packet unchanged; APRS-IS handles distribution.
        Callsign item = Assert.Single(isTarget.Packet.Path);
        Assert.Equal("WIDE2", item.Base);
        Assert.Equal(2, isTarget.Packet.Path[0].Ssid);
    }

    [Fact]
    public void GetDigipeatTargets_RfToRf_ForwardsWhenHopRemains()
    {
        DigipeaterService service = CreateService(callsignBase: "MYCALL", ssid: 9);

        PositionPacket packet = CreatePositionPacket() with
        {
            Source = new Callsign("N5RF"),
            Path = [new Callsign("WIDE2", 2)]
        };

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts());

        DigipeatTarget rfTarget = Assert.Single(targets, t => t.PortId == RfPortId);
        Assert.Equal(2, rfTarget.Packet.Path.Count);
        Assert.Equal("MYCALL", rfTarget.Packet.Path[0].Base);
        Assert.Equal("WIDE2", rfTarget.Packet.Path[1].Base);
        Assert.Equal(1, rfTarget.Packet.Path[1].Ssid);
    }

    [Fact]
    public void GetDigipeatTargets_RfToRf_Wide1NotResponded_NotForwardedOnRf()
    {
        DigipeaterService service = CreateService(callsignBase: "MYCALL", respondToWide1: false, respondToWide2: true);

        PositionPacket packet = CreatePositionPacket() with
        {
            Source = new Callsign("N5RF"),
            Path = [new Callsign("WIDE1", 1)]
        };

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts());

        Assert.DoesNotContain(targets, t => t.PortId == RfPortId);
    }

    [Fact]
    public void GetDigipeatTargets_RfToRf_Wide1Responded_ForwardsWithDecrement()
    {
        DigipeaterService service = CreateService(callsignBase: "MYCALL", respondToWide1: true, respondToWide2: false);

        PositionPacket packet = CreatePositionPacket() with
        {
            Source = new Callsign("N5RF"),
            Path = [new Callsign("WIDE1", 1)]
        };

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts());

        DigipeatTarget rfTarget = Assert.Single(targets, t => t.PortId == RfPortId);
        Assert.Equal("MYCALL", rfTarget.Packet.Path[0].Base);
        Assert.Equal("WIDE1", rfTarget.Packet.Path[1].Base);
        Assert.Equal(0, rfTarget.Packet.Path[1].Ssid);
    }

    [Fact]
    public void GetDigipeatTargets_RfToRf_TraceNotResponded_NotForwardedOnRf()
    {
        DigipeaterService service = CreateService(callsignBase: "MYCALL", respondToTrace: false);

        PositionPacket packet = CreatePositionPacket() with
        {
            Source = new Callsign("N5RF"),
            Path = [new Callsign("TRACE", 3)]
        };

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts());

        Assert.DoesNotContain(targets, t => t.PortId == RfPortId);
    }

    [Fact]
    public void GetDigipeatTargets_RfToRf_TraceResponded_ForwardsWithDecrement()
    {
        DigipeaterService service = CreateService(callsignBase: "MYCALL", respondToTrace: true);

        PositionPacket packet = CreatePositionPacket() with
        {
            Source = new Callsign("N5RF"),
            Path = [new Callsign("TRACE", 3)]
        };

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts());

        DigipeatTarget rfTarget = Assert.Single(targets, t => t.PortId == RfPortId);
        Assert.Equal("MYCALL", rfTarget.Packet.Path[0].Base);
        Assert.Equal("TRACE", rfTarget.Packet.Path[1].Base);
        Assert.Equal(2, rfTarget.Packet.Path[1].Ssid);
    }

    [Fact]
    public void GetDigipeatTargets_RfToRf_AlreadyRepeatedHop_IsSkipped()
    {
        DigipeaterService service = CreateService(callsignBase: "MYCALL", ssid: 9, respondToWide2: true);

        // WIDE1-1 already marked "has been repeated" by an upstream digipeater, so
        // this station must skip it and service the next unserviced WIDE2-2 hop.
        PositionPacket packet = CreatePositionPacket() with
        {
            Source = new Callsign("N5RF"),
            Path =
            [
                new Callsign("WIDE1", 1, hasBeenRepeated: true),
                new Callsign("WIDE2", 2)
            ]
        };

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts());

        DigipeatTarget rfTarget = Assert.Single(targets, t => t.PortId == RfPortId);
        Assert.Equal(3, rfTarget.Packet.Path.Count);
        Assert.Equal("WIDE1", rfTarget.Packet.Path[0].Base);
        Assert.True(rfTarget.Packet.Path[0].HasBeenRepeated);
        Assert.Equal("MYCALL", rfTarget.Packet.Path[1].Base);
        Assert.Equal("WIDE2", rfTarget.Packet.Path[2].Base);
        Assert.Equal(1, rfTarget.Packet.Path[2].Ssid);
    }

    [Fact]
    public void GetDigipeatTargets_RfToRf_InsertCallsignDisabled_OmitsOwnCallsign()
    {
        DigipeaterService service = CreateService(
            callsignBase: "MYCALL",
            ssid: 9,
            respondToWide2: true,
            digipeaterInsertCallsign: false);

        PositionPacket packet = CreatePositionPacket() with
        {
            Source = new Callsign("N5RF"),
            Path = [new Callsign("WIDE2", 2)]
        };

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts());

        DigipeatTarget rfTarget = Assert.Single(targets, t => t.PortId == RfPortId);
        Callsign item = Assert.Single(rfTarget.Packet.Path);
        Assert.Equal("WIDE2", item.Base);
        Assert.Equal(1, rfTarget.Packet.Path[0].Ssid);
    }

    [Fact]
    public void GetDigipeatTargets_RfToRf_TraceAlwaysInsertsCallsign()
    {
        DigipeaterService service = CreateService(
            callsignBase: "MYCALL",
            ssid: 9,
            respondToTrace: true,
            digipeaterInsertCallsign: false);

        PositionPacket packet = CreatePositionPacket() with
        {
            Source = new Callsign("N5RF"),
            Path = [new Callsign("TRACE", 3)]
        };

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts());

        DigipeatTarget rfTarget = Assert.Single(targets, t => t.PortId == RfPortId);
        Assert.Equal(2, rfTarget.Packet.Path.Count);
        Assert.Equal("MYCALL", rfTarget.Packet.Path[0].Base);
        Assert.Equal("TRACE", rfTarget.Packet.Path[1].Base);
        Assert.Equal(2, rfTarget.Packet.Path[1].Ssid);
    }

    [Fact]
    public void GetDigipeatTargets_RfToRf_ServicedHopIsMarkedRepeated()
    {
        DigipeaterService service = CreateService(callsignBase: "MYCALL", ssid: 9, respondToWide2: true);

        PositionPacket packet = CreatePositionPacket() with
        {
            Source = new Callsign("N5RF"),
            Path = [new Callsign("WIDE2", 2)]
        };

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts());

        DigipeatTarget rfTarget = Assert.Single(targets, t => t.PortId == RfPortId);
        Assert.Equal(2, rfTarget.Packet.Path.Count);

        // Our inserted callsign and the decremented hop are both marked as repeated.
        Assert.Equal("MYCALL", rfTarget.Packet.Path[0].Base);
        Assert.True(rfTarget.Packet.Path[0].HasBeenRepeated);
        Assert.Equal("WIDE2", rfTarget.Packet.Path[1].Base);
        Assert.Equal(1, rfTarget.Packet.Path[1].Ssid);
        Assert.True(rfTarget.Packet.Path[1].HasBeenRepeated);
    }

    [Fact]
    public void GetDigipeatTargets_PortWithoutTx_IsSkipped()
    {
        DigipeaterService service = CreateService();
        List<PortInfo> ports =
        [
            new(RfPortId, IsAprsIs: false, IsTxEnabled: false, AllowDigipeat: true)
        ];

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            ports);

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_PortWithoutAllowDigipeat_IsSkipped()
    {
        DigipeaterService service = CreateService();
        List<PortInfo> ports =
        [
            new(RfPortId, IsAprsIs: false, IsTxEnabled: true, AllowDigipeat: false)
        ];

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            ports);

        Assert.Empty(targets);
    }

    [Fact]
    public void GetDigipeatTargets_SourcePort_IsExcluded()
    {
        DigipeaterService service = CreateService();
        List<PortInfo> ports =
        [
            new(SourcePortId, IsAprsIs: false, IsTxEnabled: true, AllowDigipeat: true)
        ];

        IReadOnlyList<DigipeatTarget> targets = service.GetDigipeatTargets(
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
        byte ssid = 0,
        bool respondToWide1 = false,
        bool respondToWide2 = true,
        bool respondToTrace = false,
        bool digipeaterInsertCallsign = true,
        TimeProvider? timeProvider = null)
    {
        AppSettings settings = new()
        {
            Aprs = new AprsSettings
            {
                EnableDigipeater = enableDigipeater,
                EnableAprsIsToRfGate = enableAprsIsToRfGate,
                EnableRfToAprsIsGate = true,
                Callsign = callsignBase,
                Ssid = (AprsSsid)ssid,
                DigipeaterRespondToWide1 = respondToWide1,
                DigipeaterRespondToWide2 = respondToWide2,
                DigipeaterRespondToTrace = respondToTrace,
                DigipeaterInsertCallsign = digipeaterInsertCallsign,
            },
        };

        return new DigipeaterService(
            new TestConfigurationService(settings),
            NullLogger<DigipeaterService>.Instance,
            timeProvider);
    }

    private sealed class TestConfigurationService(AppSettings settings) : IConfigurationService
    {

#pragma warning disable CS0067 // Event is never used in this test double
        public event EventHandler? SettingsChanged;
#pragma warning restore CS0067

        public AppSettings Current { get; private set; } = settings;

        public Task<AppSettings> UpdateAsync(
            Func<AppSettings, AppSettings> update,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current = update(Current));
    }
}

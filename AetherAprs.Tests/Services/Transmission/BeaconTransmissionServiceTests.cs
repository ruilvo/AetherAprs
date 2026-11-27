// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.Packets;
using AetherAprs.Localization;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Beaconing;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Ports;
using AetherAprs.Services.Transmission;
using Geo;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Transmission;

public sealed class BeaconTransmissionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsUsableFix_RejectsAMissingFix()
    {
        Assert.False(BeaconTransmissionService.IsUsableFix(null, Now, out string reason));
        Assert.Equal("no fix available", reason);
    }

    [Fact]
    public void IsUsableFix_RejectsTheNullIslandPlaceholder()
    {
        LocationData fix = CreateFix(0, 0);

        Assert.False(BeaconTransmissionService.IsUsableFix(fix, Now, out string reason));
        Assert.Contains("null-island", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void IsUsableFix_RejectsAFixOlderThanTheFreshnessWindow()
    {
        LocationData fix = CreateFix(
            38.7223,
            -9.1393,
            timestamp: Now - BeaconTransmissionService.MaximumFixAge - TimeSpan.FromSeconds(1));

        Assert.False(BeaconTransmissionService.IsUsableFix(fix, Now, out string reason));
        Assert.Contains("older", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void IsUsableFix_RejectsAFixWithPoorAccuracy()
    {
        LocationData fix = CreateFix(
            38.7223,
            -9.1393,
            accuracy: BeaconTransmissionService.MaximumAccuracyMeters + 1);

        Assert.False(BeaconTransmissionService.IsUsableFix(fix, Now, out string reason));
        Assert.Contains("accuracy", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void IsUsableFix_AcceptsAFreshAccurateFix()
    {
        LocationData fix = CreateFix(38.7223, -9.1393);

        Assert.True(BeaconTransmissionService.IsUsableFix(fix, Now, out string reason));
        Assert.Empty(reason);
    }

    [Fact]
    public async Task SendManualBeacon_WithOneFailingPort_StillReportsSuccess()
    {
        PortConfig goodPort = CreatePort("Good port");
        PortConfig badPort = CreatePort("Bad port");
        IPortService portService = Substitute.For<IPortService>();
        portService.Ports.Returns([goodPort, badPort]);
        portService.SendPacketAsync(goodPort.Id, Arg.Any<AprsPacket>()).Returns(Task.CompletedTask);
        portService.SendPacketAsync(badPort.Id, Arg.Any<AprsPacket>())
            .Returns(Task.FromException(new InvalidOperationException("modem offline")));

        using BeaconTransmissionService service = CreateService(portService);

        await service.SendManualBeaconAsync(CreateFix(38.7223, -9.1393));

        // Each port gets its own attempt; one failure is not the whole beacon failing.
        await portService.Received(1).SendPacketAsync(goodPort.Id, Arg.Any<AprsPacket>());
        await portService.Received(1).SendPacketAsync(badPort.Id, Arg.Any<AprsPacket>());
        Assert.Equal(Strings.Get("ManualBeaconSent"), service.BeaconStatus);
    }

    private static BeaconTransmissionService CreateService(IPortService portService)
    {
        IBeaconService beaconService = Substitute.For<IBeaconService>();
        beaconService.CurrentConfiguration.Returns(BeaconConfig.CreateWalkPreset());

        IConfigurationService configurationService = Substitute.For<IConfigurationService>();
        configurationService.Current.Returns(new AppSettings
        {
            Aprs = new AprsSettings { Callsign = "N0CALL" }
        });

        IAprsPortSettingsResolver resolver = Substitute.For<IAprsPortSettingsResolver>();
        resolver.GetCallsign(Arg.Any<string>()).Returns(call => call.Arg<string>());

        IPacketFactory packetFactory = Substitute.For<IPacketFactory>();
        packetFactory.CreatePositionPacket(
            Arg.Any<LocationData>(),
            Arg.Any<string>(),
            Arg.Any<SymbolTable>(),
            Arg.Any<SymbolCode>(),
            Arg.Any<double?>(),
            Arg.Any<string?>()).Returns(CreatePacket());

        return new BeaconTransmissionService(
            beaconService,
            portService,
            configurationService,
            resolver,
            packetFactory,
            NullLogger<BeaconTransmissionService>.Instance,
            queue: null,
            timeProvider: new FixedTimeProvider(Now));
    }

    private static PositionPacket CreatePacket() => new()
    {
        Source = new Callsign("N0CALL"),
        Destination = new Callsign("APZAEP"),
        Location = new Coordinate(38.7223, -9.1393),
        Symbol = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign),
        Format = PositionFormat.Uncompressed
    };

    private static PortConfig CreatePort(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        IsEnabled = true,
        IsRx = true,
        IsTx = true,
        TypeSettings = new AprsIsSettings()
    };

    private static LocationData CreateFix(
        double latitude,
        double longitude,
        double accuracy = 10,
        DateTimeOffset? timestamp = null) => new()
        {
            Location = new Coordinate(latitude, longitude),
            Accuracy = accuracy,
            Timestamp = timestamp ?? Now
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

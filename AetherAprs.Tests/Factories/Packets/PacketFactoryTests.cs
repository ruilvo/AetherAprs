// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.Packets;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Configuration;
using Geo;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Factories.Packets;

public sealed class PacketFactoryTests
{
    private const string DefaultBeaconComment = "AetherAprs beacon";

    /// <summary>
    /// The AX.25 destination software identifier expected on every transmitted packet.
    /// Kept as a literal so the test fails if the production constant changes silently.
    /// </summary>
    private const string ExpectedSoftwareIdentifier = "APZAEP";

    private static IPacketFactory CreateFactory(
        string? digipeaterPath = null,
        string beaconComment = DefaultBeaconComment,
        PositionFormat positionFormat = PositionFormat.Compressed)
    {
        var configuration = Substitute.For<IConfigurationService>();
        configuration.Current.Returns(new AppSettings
        {
            Aprs = new AprsSettings
            {
                Callsign = "N0CALL",
                Ssid = AprsSsid.PrimaryStation,
                BeaconComment = beaconComment,
                PositionFormat = positionFormat,
                DigipeaterPath = digipeaterPath ?? string.Empty
            }
        });

        return new PacketFactory(configuration);
    }

    private static LocationData CreateLocation(double latitude = 38.5, double longitude = -9.0, double? altitude = 100)
    {
        return new LocationData
        {
            Location = new Coordinate(latitude, longitude),
            Altitude = altitude
        };
    }

    [Fact]
    public void CreatePositionPacket_ValidCallsign_BuildsPacket()
    {
        var factory = CreateFactory();
        var location = CreateLocation(latitude: 38.5, longitude: -9.0, altitude: 100);

        var packet = factory.CreatePositionPacket(
            location,
            "N0CALL-7",
            SymbolTable.Primary,
            SymbolCode.HyphenMinus,
            course: 90);

        Assert.Equal(new Callsign("N0CALL", 7), packet.Source);
        Assert.Equal(new Callsign(ExpectedSoftwareIdentifier), packet.Destination);
        Assert.Equal(38.5, packet.Location.Latitude);
        Assert.Equal(-9.0, packet.Location.Longitude);
        Assert.Equal(90, packet.Course);
        Assert.Equal(new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus), packet.Symbol);
        Assert.Equal(PositionFormat.Compressed, packet.Format);
        Assert.Equal(3, packet.Precision);
        Assert.Empty(packet.Path);
    }

    [Fact]
    public void CreatePositionPacket_UncompressedFormat_UsesPrecision2()
    {
        var factory = CreateFactory(positionFormat: PositionFormat.Uncompressed);

        var packet = factory.CreatePositionPacket(
            CreateLocation(),
            "N0CALL",
            SymbolTable.Primary,
            SymbolCode.HyphenMinus);

        Assert.Equal(PositionFormat.Uncompressed, packet.Format);
        Assert.Equal(2, packet.Precision);
    }

    [Fact]
    public void CreatePositionPacket_MicEFormat_PropagatesFormat()
    {
        var factory = CreateFactory(positionFormat: PositionFormat.MicE);

        var packet = factory.CreatePositionPacket(
            CreateLocation(),
            "N0CALL",
            SymbolTable.Primary,
            SymbolCode.HyphenMinus);

        Assert.Equal(PositionFormat.MicE, packet.Format);
        Assert.Equal(2, packet.Precision);
    }

    [Fact]
    public void CreatePositionPacket_ConvertsAltitudeMetersToFeet()
    {
        var factory = CreateFactory();
        var location = CreateLocation(altitude: 100);

        var packet = factory.CreatePositionPacket(
            location,
            "N0CALL",
            SymbolTable.Primary,
            SymbolCode.HyphenMinus);

        Assert.NotNull(packet.Altitude);
        Assert.Equal(100 * 3.28084, packet.Altitude.Value, 3);
    }

    [Fact]
    public void CreatePositionPacket_NoAltitude_LeavesAltitudeNull()
    {
        var factory = CreateFactory();
        var location = CreateLocation(altitude: null);

        var packet = factory.CreatePositionPacket(
            location,
            "N0CALL",
            SymbolTable.Primary,
            SymbolCode.HyphenMinus);

        Assert.Null(packet.Altitude);
    }

    [Fact]
    public void CreatePositionPacket_NoComment_FallsBackToBeaconComment()
    {
        var factory = CreateFactory();

        var packet = factory.CreatePositionPacket(
            CreateLocation(),
            "N0CALL",
            SymbolTable.Primary,
            SymbolCode.HyphenMinus);

        Assert.Equal(DefaultBeaconComment, packet.Comment);
    }

    [Fact]
    public void CreatePositionPacket_WhitespaceComment_FallsBackToBeaconComment()
    {
        var factory = CreateFactory();

        var packet = factory.CreatePositionPacket(
            CreateLocation(),
            "N0CALL",
            SymbolTable.Primary,
            SymbolCode.HyphenMinus,
            comment: "   ");

        Assert.Equal(DefaultBeaconComment, packet.Comment);
    }

    [Fact]
    public void CreatePositionPacket_ProvidedComment_OverridesBeaconComment()
    {
        var factory = CreateFactory();

        var packet = factory.CreatePositionPacket(
            CreateLocation(),
            "N0CALL",
            SymbolTable.Primary,
            SymbolCode.HyphenMinus,
            comment: "Custom comment");

        Assert.Equal("Custom comment", packet.Comment);
    }

    [Fact]
    public void CreatePositionPacket_DigipeaterPathConfigured_AppliesParsedPath()
    {
        var factory = CreateFactory(digipeaterPath: "WIDE1-1,WIDE2-1");

        var packet = factory.CreatePositionPacket(
            CreateLocation(),
            "N0CALL",
            SymbolTable.Primary,
            SymbolCode.HyphenMinus);

        Assert.Equal(2, packet.Path.Count);
        Assert.Equal(new Callsign("WIDE1", 1), packet.Path[0]);
        Assert.Equal(new Callsign("WIDE2", 1), packet.Path[1]);
    }

    [Fact]
    public void CreatePositionPacket_WhitespaceDigipeaterPath_YieldsEmptyPath()
    {
        var factory = CreateFactory(digipeaterPath: "   ");

        var packet = factory.CreatePositionPacket(
            CreateLocation(),
            "N0CALL",
            SymbolTable.Primary,
            SymbolCode.HyphenMinus);

        Assert.Empty(packet.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NOT-VALID!")]
    public void CreatePositionPacket_InvalidCallsign_ThrowsArgumentException(string callsign)
    {
        var factory = CreateFactory();

        Assert.Throws<ArgumentException>(() =>
            factory.CreatePositionPacket(
                CreateLocation(),
                callsign,
                SymbolTable.Primary,
                SymbolCode.HyphenMinus));
    }

    [Fact]
    public void CreateMessagePacket_ValidInput_BuildsPacket()
    {
        var factory = CreateFactory(digipeaterPath: "WIDE1-1");
        var addressee = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));

        var packet = factory.CreateMessagePacket("N0CALL-1", addressee, "Hello", messageNumber: 42);

        Assert.Equal(new Callsign("N0CALL", 1), packet.Source);
        Assert.Equal(new Callsign(ExpectedSoftwareIdentifier), packet.Destination);
        Assert.Equal(addressee, packet.Addressee);
        Assert.Equal("Hello", packet.Text);
        Assert.Equal(42, packet.MessageNumber);
        _ = Assert.Single(packet.Path);
        Assert.Equal(new Callsign("WIDE1", 1), packet.Path[0]);
    }

    [Fact]
    public void CreateMessagePacket_InvalidCallsign_ThrowsArgumentException()
    {
        var factory = CreateFactory();

        Assert.Throws<ArgumentException>(() =>
            factory.CreateMessagePacket("BAD!", AprsAddress.FromCallsign(new Callsign("K0OTH")), "Hello"));
    }

    [Fact]
    public void CreateAckPacket_ValidInput_BuildsPacket()
    {
        var factory = CreateFactory(digipeaterPath: "WIDE1-1");
        var addressee = AprsAddress.FromCallsign(new Callsign("K0OTH"));

        var packet = factory.CreateAckPacket("N0CALL-1", addressee, 5);

        Assert.Equal(new Callsign("N0CALL", 1), packet.Source);
        Assert.Equal(new Callsign(ExpectedSoftwareIdentifier), packet.Destination);
        Assert.Equal(addressee, packet.Addressee);
        Assert.Equal(5, packet.MessageNumber);
        _ = Assert.Single(packet.Path);
        Assert.Equal(new Callsign("WIDE1", 1), packet.Path[0]);
    }

    [Fact]
    public void CreateAckPacket_InvalidCallsign_ThrowsArgumentException()
    {
        var factory = CreateFactory();

        Assert.Throws<ArgumentException>(() =>
            factory.CreateAckPacket("BAD!", AprsAddress.FromCallsign(new Callsign("K0OTH")), 1));
    }

    [Fact]
    public void CreateRejPacket_ValidInput_BuildsPacket()
    {
        var factory = CreateFactory(digipeaterPath: "WIDE1-1");
        var addressee = AprsAddress.FromCallsign(new Callsign("K0OTH"));

        var packet = factory.CreateRejPacket("N0CALL-1", addressee, 6);

        Assert.Equal(new Callsign("N0CALL", 1), packet.Source);
        Assert.Equal(new Callsign(ExpectedSoftwareIdentifier), packet.Destination);
        Assert.Equal(addressee, packet.Addressee);
        Assert.Equal(6, packet.MessageNumber);
        _ = Assert.Single(packet.Path);
        Assert.Equal(new Callsign("WIDE1", 1), packet.Path[0]);
    }

    [Fact]
    public void CreateRejPacket_InvalidCallsign_ThrowsArgumentException()
    {
        var factory = CreateFactory();

        Assert.Throws<ArgumentException>(() =>
            factory.CreateRejPacket("BAD!", AprsAddress.FromCallsign(new Callsign("K0OTH")), 1));
    }

    [Fact]
    public void CreateEveryPacketType_UsesSoftwareIdentifierDestinationAndConfiguredPath()
    {
        var factory = CreateFactory(digipeaterPath: "WIDE1-1,WIDE2-1");
        var addressee = AprsAddress.FromCallsign(new Callsign("K0OTH", 7));
        var location = CreateLocation();

        var packets = new AprsPacket[]
        {
            factory.CreatePositionPacket(
                location, "N0CALL-1", SymbolTable.Primary, SymbolCode.HyphenMinus),
            factory.CreateMessagePacket("N0CALL-1", addressee, "Hello", messageNumber: 1),
            factory.CreateAckPacket("N0CALL-1", addressee, 1),
            factory.CreateRejPacket("N0CALL-1", addressee, 1),
            factory.CreateObjectPacket(
                "N0CALL-1", "OBJ", location.Location, SymbolTable.Primary, SymbolCode.HyphenMinus),
            factory.CreateItemPacket(
                "N0CALL-1", "ITM", location.Location, SymbolTable.Primary, SymbolCode.HyphenMinus),
            factory.CreateBulletinPacket("N0CALL-1", "BLN0TEST", "Bulletin text")
        };

        Assert.Equal(7, packets.Length);

        foreach (var packet in packets)
        {
            // The AX.25 destination carries the APRS software identifier, not "APRS".
            Assert.Equal(ExpectedSoftwareIdentifier, packet.Destination.Base);
            Assert.Null(packet.Destination.Ssid);

            Assert.Equal(2, packet.Path.Count);
            Assert.Equal(new Callsign("WIDE1", 1), packet.Path[0]);
            Assert.Equal(new Callsign("WIDE2", 1), packet.Path[1]);
        }
    }

    [Fact]
    public void CreateEveryPacketType_InvalidCallsign_ThrowsArgumentExceptionWithSharedMessageShape()
    {
        const string invalidCallsign = "BAD!";

        var factory = CreateFactory();
        var addressee = AprsAddress.FromCallsign(new Callsign("K0OTH"));
        var location = CreateLocation();

        var cases = new (string PacketType, Func<AprsPacket> Create)[]
        {
            ("position", () => factory.CreatePositionPacket(
                location, invalidCallsign, SymbolTable.Primary, SymbolCode.HyphenMinus)),
            ("message", () => factory.CreateMessagePacket(invalidCallsign, addressee, "Hello")),
            ("ack", () => factory.CreateAckPacket(invalidCallsign, addressee, 1)),
            ("rej", () => factory.CreateRejPacket(invalidCallsign, addressee, 1)),
            ("object", () => factory.CreateObjectPacket(
                invalidCallsign, "OBJ", location.Location, SymbolTable.Primary, SymbolCode.HyphenMinus)),
            ("item", () => factory.CreateItemPacket(
                invalidCallsign, "ITM", location.Location, SymbolTable.Primary, SymbolCode.HyphenMinus)),
            ("bulletin", () => factory.CreateBulletinPacket(invalidCallsign, "BLN0TEST", "Text"))
        };

        Assert.Equal(7, cases.Length);

        foreach (var @case in cases)
        {
            var exception = Assert.Throws<ArgumentException>(() => @case.Create());

            Assert.True(
                exception.Message.StartsWith(
                    $"Invalid callsign format: {invalidCallsign}",
                    StringComparison.Ordinal),
                $"{@case.PacketType} packet must keep the shared invalid-callsign message shape.");
        }
    }
}

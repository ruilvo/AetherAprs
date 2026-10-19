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

    private static IPacketFactory CreateFactory(
        string? digipeaterPath = null,
        string beaconComment = DefaultBeaconComment)
    {
        var configuration = Substitute.For<IConfigurationService>();
        configuration.Settings.Returns(new AppSettings
        {
            Aprs = new AprsSettings
            {
                Callsign = "N0CALL",
                Ssid = AprsSsid.PrimaryStation,
                BeaconComment = beaconComment,
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
        Assert.Equal(new Callsign("APRS"), packet.Destination);
        Assert.Equal(38.5, packet.Location.Latitude);
        Assert.Equal(-9.0, packet.Location.Longitude);
        Assert.Equal(90, packet.Course);
        Assert.Equal(new Symbol(SymbolTable.Primary, SymbolCode.HyphenMinus), packet.Symbol);
        Assert.Equal(2, packet.Precision);
        Assert.Empty(packet.Path);
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
        var addressee = new Callsign("K0OTH", 7);

        var packet = factory.CreateMessagePacket("N0CALL-1", addressee, "Hello", messageNumber: 42);

        Assert.Equal(new Callsign("N0CALL", 1), packet.Source);
        Assert.Equal(new Callsign("APRS"), packet.Destination);
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
            factory.CreateMessagePacket("BAD!", new Callsign("K0OTH"), "Hello"));
    }

    [Fact]
    public void CreateAckPacket_ValidInput_BuildsPacket()
    {
        var factory = CreateFactory(digipeaterPath: "WIDE1-1");
        var addressee = new Callsign("K0OTH");

        var packet = factory.CreateAckPacket("N0CALL-1", addressee, 5);

        Assert.Equal(new Callsign("N0CALL", 1), packet.Source);
        Assert.Equal(new Callsign("APRS"), packet.Destination);
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
            factory.CreateAckPacket("BAD!", new Callsign("K0OTH"), 1));
    }

    [Fact]
    public void CreateRejPacket_ValidInput_BuildsPacket()
    {
        var factory = CreateFactory(digipeaterPath: "WIDE1-1");
        var addressee = new Callsign("K0OTH");

        var packet = factory.CreateRejPacket("N0CALL-1", addressee, 6);

        Assert.Equal(new Callsign("N0CALL", 1), packet.Source);
        Assert.Equal(new Callsign("APRS"), packet.Destination);
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
            factory.CreateRejPacket("BAD!", new Callsign("K0OTH"), 1));
    }
}

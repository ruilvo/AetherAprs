// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text;
using AetherAprs.Models.Aprs;
using AetherAprs.Modems.Aprs;
using Xunit;

namespace AetherAprs.Tests.Aprs;

public class AprsSerializerTests
{
    private static readonly Callsign Source = new("N0CALL");
    private static readonly Callsign Dest = new("APZ001");

    // ---------------------------------------------------------------
    // Position serialization
    // ---------------------------------------------------------------

    [Fact]
    public void Serialize_PositionPacket_ProducesValidAx25()
    {
        var packet = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Raw = "!3830.00N/00906.00W#",
            Latitude = 38.5,
            Longitude = -9.1,
            Symbol = new Symbol('/', '#'),
            Precision = 2
        };

        var bytes = AprsSerializer.Serialize(packet, Source, Dest);

        // Round-trip: parse the serialized bytes back
        var parsed = AprsParser.ParseFrame(bytes);
        var pos = Assert.IsType<PositionPacket>(parsed);
        Assert.Equal(38.5, pos.Latitude, 4);
        Assert.Equal(-9.1, pos.Longitude, 4);
        Assert.Equal('/', pos.Symbol.Table);
        Assert.Equal('#', pos.Symbol.Code);
    }

    [Fact]
    public void Serialize_PositionWithComment_RoundTrips()
    {
        var packet = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Raw = string.Empty,
            Latitude = 38.5,
            Longitude = -9.1,
            Symbol = new Symbol('/', '#'),
            Comment = "Test Comment",
            Precision = 2
        };

        var bytes = AprsSerializer.Serialize(packet, Source, Dest);
        var parsed = AprsParser.ParseFrame(bytes);
        var pos = Assert.IsType<PositionPacket>(parsed);
        Assert.Equal("Test Comment", pos.Comment);
    }

    [Fact]
    public void Serialize_PositionWithSouthernLatitude_RoundTrips()
    {
        var packet = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Latitude = -33.5,
            Longitude = 151.0,
            Symbol = new Symbol('/', '>'),
            Precision = 2
        };

        var bytes = AprsSerializer.Serialize(packet, Source, Dest);
        var parsed = AprsParser.ParseFrame(bytes);
        var pos = Assert.IsType<PositionPacket>(parsed);
        Assert.True(pos.Latitude < 0);
        Assert.Equal(-33.5, pos.Latitude, 4);
    }

    // ---------------------------------------------------------------
    // Message serialization
    // ---------------------------------------------------------------

    [Fact]
    public void Serialize_MessagePacket_RoundTrips()
    {
        var packet = new MessagePacket
        {
            Source = Source,
            Destination = Dest,
            Addressee = new Callsign("OTHER"),
            Text = "Hello there"
        };

        var bytes = AprsSerializer.Serialize(packet, Source, Dest);
        var parsed = AprsParser.ParseFrame(bytes);
        var msg = Assert.IsType<MessagePacket>(parsed);
        Assert.Equal(new Callsign("OTHER"), msg.Addressee);
        Assert.Equal("Hello there", msg.Text);
        Assert.Null(msg.MessageNumber);
    }

    [Fact]
    public void Serialize_MessageWithNumber_RoundTrips()
    {
        var packet = new MessagePacket
        {
            Source = Source,
            Destination = Dest,
            Addressee = new Callsign("OTHER"),
            Text = "Hello",
            MessageNumber = 42
        };

        var bytes = AprsSerializer.Serialize(packet, Source, Dest);
        var parsed = AprsParser.ParseFrame(bytes);
        var msg = Assert.IsType<MessagePacket>(parsed);
        Assert.Equal("Hello", msg.Text);
        Assert.Equal(42, msg.MessageNumber);
    }

    // ---------------------------------------------------------------
    // Status serialization
    // ---------------------------------------------------------------

    [Fact]
    public void Serialize_StatusPacket_RoundTrips()
    {
        var packet = new StatusPacket
        {
            Source = Source,
            Destination = Dest,
            Text = "Online via APRS"
        };

        var bytes = AprsSerializer.Serialize(packet, Source, Dest);
        var parsed = AprsParser.ParseFrame(bytes);
        var status = Assert.IsType<StatusPacket>(parsed);
        Assert.Equal("Online via APRS", status.Text);
    }

    // ---------------------------------------------------------------
    // Weather serialization
    // ---------------------------------------------------------------

    [Fact]
    public void Serialize_WeatherPacket_RoundTrips()
    {
        var packet = new WeatherPacket
        {
            Source = Source,
            Destination = Dest,
            WindDirection = 90,
            WindSpeed = 15,
            Temperature = 72,
            Humidity = 50,
            Pressure = 10130
        };

        var bytes = AprsSerializer.Serialize(packet, Source, Dest);
        var parsed = AprsParser.ParseFrame(bytes);
        var w = Assert.IsType<WeatherPacket>(parsed);
        Assert.Equal(90, w.WindDirection);
        Assert.Equal(15, w.WindSpeed);
        Assert.Equal(72, w.Temperature);
        Assert.Equal(50, w.Humidity);
        Assert.Equal(10130, w.Pressure);
        Assert.Null(w.WindGust);
        Assert.Null(w.Rain1h);
    }

    [Fact]
    public void Serialize_WeatherEmpty_OnlyProducesUnderscore()
    {
        var packet = new WeatherPacket
        {
            Source = Source,
            Destination = Dest
        };

        var bytes = AprsSerializer.Serialize(packet, Source, Dest);
        var parsed = AprsParser.ParseFrame(bytes);
        var w = Assert.IsType<WeatherPacket>(parsed);
        Assert.Null(w.WindDirection);
        Assert.Null(w.WindSpeed);
    }

    // ---------------------------------------------------------------
    // Unknown serialization
    // ---------------------------------------------------------------

    [Fact]
    public void Serialize_UnknownPacket_RoundTrips()
    {
        var packet = new UnknownPacket
        {
            Source = Source,
            Destination = Dest,
            Raw = "$SOME,WEIRD,DATA"
        };

        var bytes = AprsSerializer.Serialize(packet, Source, Dest);
        var parsed = AprsParser.ParseFrame(bytes);
        var unknown = Assert.IsType<UnknownPacket>(parsed);
        Assert.Equal("$SOME,WEIRD,DATA", unknown.Raw);
    }

    // ---------------------------------------------------------------
    // IAprsPacket interface dispatch
    // ---------------------------------------------------------------

    [Fact]
    public void Serialize_DispatchesOnConcreteType()
    {
        IAprsPacket packet = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Latitude = 38.5,
            Longitude = -9.10,
            Symbol = new Symbol('/', '#'),
            Precision = 2
        };

        var bytes = AprsSerializer.Serialize(packet, Source, Dest);
        var parsed = AprsParser.ParseFrame(bytes);
        Assert.IsType<PositionPacket>(parsed);
    }

    [Fact]
    public void Serialize_NullPacket_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => AprsSerializer.Serialize(null!, Source, Dest));
        Assert.Contains("packet", ex.Message, StringComparison.Ordinal);
    }
}
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
    public void FormatInfoField_PositionPacket_ProducesCorrectInfoField()
    {
        var packet = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Raw = "!3830.00N/00906.00W#",
            Latitude = 38.5,
            Longitude = -9.1,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Precision = 2
        };

        var infoField = AprsSerializer.FormatInfoField(packet);

        Assert.StartsWith("!3830.00N/", infoField);
        Assert.Contains("00906.00W", infoField);
        Assert.EndsWith("#", infoField);
    }

    [Fact]
    public void FormatInfoField_PositionWithComment_IncludesComment()
    {
        var packet = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Latitude = 38.5,
            Longitude = -9.1,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Comment = "Test Comment",
            Precision = 2
        };

        var infoField = AprsSerializer.FormatInfoField(packet);

        Assert.Contains("Test Comment", infoField);
    }

    [Fact]
    public void FormatInfoField_PositionWithSouthernLatitude_ProducesS()
    {
        var packet = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Latitude = -33.5,
            Longitude = 151.0,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.GreaterThanSign),
            Precision = 2
        };

        var infoField = AprsSerializer.FormatInfoField(packet);

        Assert.Contains('S', infoField);
    }

    [Fact]
    public void Serialize_And_Parse_RoundTrip_Works()
    {
        var packet = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Latitude = 38.5,
            Longitude = -9.1,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Precision = 2
        };

        var bytes = Ax25Serializer.Serialize(packet);
        var parsed = Ax25Parser.ParseFrame(bytes);

        var pos = Assert.IsType<PositionPacket>(parsed);
        Assert.Equal(38.5, pos.Latitude, 4);
        Assert.Equal(-9.1, pos.Longitude, 4);
        Assert.Equal(SymbolTable.Primary, pos.Symbol.Table);
        Assert.Equal(SymbolCode.NumberSign, pos.Symbol.Code);
    }

    // ---------------------------------------------------------------
    // Message serialization
    // ---------------------------------------------------------------

    [Fact]
    public void FormatInfoField_MessagePacket_ProducesCorrectInfoField()
    {
        var packet = new MessagePacket
        {
            Source = Source,
            Destination = Dest,
            Addressee = new Callsign("OTHER"),
            Text = "Hello there"
        };

        var infoField = AprsSerializer.FormatInfoField(packet);

        Assert.StartsWith(":OTHER    :", infoField);
        Assert.EndsWith("Hello there", infoField);
    }

    [Fact]
    public void FormatInfoField_MessageWithNumber_IncludesNumber()
    {
        var packet = new MessagePacket
        {
            Source = Source,
            Destination = Dest,
            Addressee = new Callsign("OTHER"),
            Text = "Hello",
            MessageNumber = 42
        };

        var infoField = AprsSerializer.FormatInfoField(packet);

        Assert.Equal(":OTHER    :Hello{42}", infoField);
    }

    [Fact]
    public void Serialize_Message_RoundTrips()
    {
        var packet = new MessagePacket
        {
            Source = Source,
            Destination = Dest,
            Addressee = new Callsign("OTHER"),
            Text = "Hello there"
        };

        var bytes = Ax25Serializer.Serialize(packet);
        var parsed = Ax25Parser.ParseFrame(bytes);

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

        var bytes = Ax25Serializer.Serialize(packet);
        var parsed = Ax25Parser.ParseFrame(bytes);

        var msg = Assert.IsType<MessagePacket>(parsed);
        Assert.Equal("Hello", msg.Text);
        Assert.Equal(42, msg.MessageNumber);
    }

    // ---------------------------------------------------------------
    // Status serialization
    // ---------------------------------------------------------------

    [Fact]
    public void FormatInfoField_StatusPacket_ProducesCorrectInfoField()
    {
        var packet = new StatusPacket
        {
            Source = Source,
            Destination = Dest,
            Text = "Online via APRS"
        };

        var infoField = AprsSerializer.FormatInfoField(packet);

        Assert.Equal(">Online via APRS", infoField);
    }

    [Fact]
    public void Serialize_Status_RoundTrips()
    {
        var packet = new StatusPacket
        {
            Source = Source,
            Destination = Dest,
            Text = "Online via APRS"
        };

        var bytes = Ax25Serializer.Serialize(packet);
        var parsed = Ax25Parser.ParseFrame(bytes);

        var status = Assert.IsType<StatusPacket>(parsed);
        Assert.Equal("Online via APRS", status.Text);
    }

    // ---------------------------------------------------------------
    // Weather serialization
    // ---------------------------------------------------------------

    [Fact]
    public void FormatInfoField_WeatherPacket_ProducesCorrectInfoField()
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

        var infoField = AprsSerializer.FormatInfoField(packet);

        Assert.StartsWith("_", infoField);
        Assert.Contains("c90", infoField);
        Assert.Contains("s15", infoField);
        Assert.Contains("t72", infoField);
        Assert.Contains("h50", infoField);
        Assert.Contains("b10130", infoField);
        Assert.DoesNotContain("g", infoField); // no gust
    }

    [Fact]
    public void FormatInfoField_WeatherEmpty_OnlyUnderscore()
    {
        var packet = new WeatherPacket
        {
            Source = Source,
            Destination = Dest
        };

        var infoField = AprsSerializer.FormatInfoField(packet);

        Assert.Equal("_", infoField);
    }

    [Fact]
    public void Serialize_Weather_RoundTrips()
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

        var bytes = Ax25Serializer.Serialize(packet);
        var parsed = Ax25Parser.ParseFrame(bytes);

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
    public void Serialize_WeatherEmpty_RoundTrips()
    {
        var packet = new WeatherPacket
        {
            Source = Source,
            Destination = Dest
        };

        var bytes = Ax25Serializer.Serialize(packet);
        var parsed = Ax25Parser.ParseFrame(bytes);

        var w = Assert.IsType<WeatherPacket>(parsed);
        Assert.Null(w.WindDirection);
        Assert.Null(w.WindSpeed);
    }

    // ---------------------------------------------------------------
    // Unknown serialization
    // ---------------------------------------------------------------

    [Fact]
    public void Serialize_Unknown_RoundTrips()
    {
        var packet = new UnknownPacket
        {
            Source = Source,
            Destination = Dest,
            Raw = "$SOME,WEIRD,DATA"
        };

        var bytes = Ax25Serializer.Serialize(packet);
        var parsed = Ax25Parser.ParseFrame(bytes);

        var unknown = Assert.IsType<UnknownPacket>(parsed);
        Assert.Equal("$SOME,WEIRD,DATA", unknown.Raw);
    }

    // ---------------------------------------------------------------
    // AprsPacket dispatch via FormatInfoField
    // ---------------------------------------------------------------

    [Fact]
    public void FormatInfoField_DispatchesOnConcreteType()
    {
        AprsPacket packet = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Latitude = 38.5,
            Longitude = -9.10,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Precision = 2
        };

        var infoField = AprsSerializer.FormatInfoField(packet);

        Assert.StartsWith("!", infoField);
    }

    [Fact]
    public void FormatInfoField_NullPacket_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => AprsSerializer.FormatInfoField(null!));
        Assert.Contains("packet", ex.Message, StringComparison.Ordinal);
    }
}
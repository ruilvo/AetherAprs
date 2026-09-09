// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models.Aprs;
using Xunit;

namespace AetherAprs.Tests.Models;

public class PacketTests
{
    private static readonly Callsign Source = new("N0CALL");
    private static readonly Callsign Dest = new("APZ001");

    [Fact]
    public void PositionPacket_DefaultPrecisionIsZero()
    {
        var pkt = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Raw = "!3850.00N/00910.00W#",
            Latitude = 38.5,
            Longitude = -9.10,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign)
        };

        Assert.Equal(PacketType.Position, pkt.Type);
        Assert.Equal(0, pkt.Precision);
        Assert.Null(pkt.Comment);
        Assert.Null(pkt.Course);
        Assert.Null(pkt.Speed);
        Assert.Null(pkt.Altitude);
    }

    [Fact]
    public void PositionPacket_WithComment_SetsComment()
    {
        var pkt = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Raw = "!3850.00N/00910.00W#Test Comment",
            Latitude = 38.5,
            Longitude = -9.10,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Comment = "Test Comment"
        };

        Assert.Equal("Test Comment", pkt.Comment);
    }

    [Fact]
    public void MessagePacket_SetsAddresseeAndText()
    {
        var pkt = new MessagePacket
        {
            Source = Source,
            Destination = Dest,
            Raw = ":N0CALL   :Hello there",
            Addressee = new Callsign("N0CALL"),
            Text = "Hello there"
        };

        Assert.Equal(PacketType.Message, pkt.Type);
        Assert.Equal(new Callsign("N0CALL"), pkt.Addressee);
        Assert.Equal("Hello there", pkt.Text);
        Assert.Null(pkt.MessageNumber);
    }

    [Fact]
    public void MessagePacket_WithMessageNumber_SetsNumber()
    {
        var pkt = new MessagePacket
        {
            Source = Source,
            Destination = Dest,
            Raw = ":N0CALL   :Hello{5}",
            Addressee = new Callsign("N0CALL"),
            Text = "Hello",
            MessageNumber = 5
        };

        Assert.Equal(5, pkt.MessageNumber);
        Assert.Equal("Hello", pkt.Text);
    }

    [Fact]
    public void StatusPacket_SetsText()
    {
        var pkt = new StatusPacket
        {
            Source = Source,
            Destination = Dest,
            Raw = ">Online via APRS",
            Text = "Online via APRS"
        };

        Assert.Equal(PacketType.Status, pkt.Type);
        Assert.Equal("Online via APRS", pkt.Text);
    }

    [Fact]
    public void WeatherPacket_DefaultAllNull()
    {
        var pkt = new WeatherPacket
        {
            Source = Source,
            Destination = Dest,
            Raw = "_"
        };

        Assert.Equal(PacketType.Weather, pkt.Type);
        Assert.Null(pkt.WindDirection);
        Assert.Null(pkt.WindSpeed);
        Assert.Null(pkt.Temperature);
        Assert.Null(pkt.Humidity);
        Assert.Null(pkt.Pressure);
    }

    [Fact]
    public void WeatherPacket_WithValues_SetsFields()
    {
        var pkt = new WeatherPacket
        {
            Source = Source,
            Destination = Dest,
            Raw = "_c100s020g030t080h55b10100",
            WindDirection = 100,
            WindSpeed = 20,
            WindGust = 30,
            Temperature = 80,
            Humidity = 55,
            Pressure = 10100
        };

        Assert.Equal(100, pkt.WindDirection);
        Assert.Equal(20, pkt.WindSpeed);
        Assert.Equal(30, pkt.WindGust);
        Assert.Equal(80, pkt.Temperature);
        Assert.Equal(55, pkt.Humidity);
        Assert.Equal(10100, pkt.Pressure);
    }

    [Fact]
    public void UnknownPacket_ContainsRawData()
    {
        var pkt = new UnknownPacket
        {
            Source = Source,
            Destination = Dest,
            Raw = "some unknown data"
        };

        Assert.Equal(PacketType.Unknown, pkt.Type);
        Assert.Equal("some unknown data", pkt.Raw);
    }

    [Fact]
    public void IAprsPacket_SwitchOnConcreteType_Works()
    {
        IAprsPacket pkt = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Raw = "!3850.00N/00910.00W#",
            Latitude = 38.5,
            Longitude = -9.10,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign)
        };

        var type = pkt switch
        {
            PositionPacket => "position",
            MessagePacket => "message",
            StatusPacket => "status",
            WeatherPacket => "weather",
            UnknownPacket => "unknown",
            _ => "other"
        };

        Assert.Equal("position", type);
    }
}
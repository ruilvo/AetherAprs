// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;

using Geo;

using Xunit;

namespace AetherAprs.Tests.Models;

public class PacketTests
{
    private static readonly Callsign Source = new("N0CALL");
    private static readonly Callsign Dest = new("APZ001");

    [Fact]
    public void PositionPacket_DefaultPrecisionIsZero()
    {
        PositionPacket pkt = new()
        {
            Source = Source,
            Destination = Dest,
            Raw = "!3850.00N/00910.00W#",
            Location = new Coordinate(38.5, -9.10),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign)
        };

        Assert.IsType<PositionPacket>(pkt);
        Assert.Equal(0, pkt.Precision);
        Assert.Null(pkt.Comment);
        Assert.Null(pkt.Course);
        Assert.Null(pkt.Speed);
        Assert.Null(pkt.Altitude);
    }

    [Fact]
    public void PositionPacket_WithComment_SetsComment()
    {
        PositionPacket pkt = new()
        {
            Source = Source,
            Destination = Dest,
            Raw = "!3850.00N/00910.00W#Test Comment",
            Location = new Coordinate(38.5, -9.10),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Comment = "Test Comment"
        };

        Assert.Equal("Test Comment", pkt.Comment);
    }

    [Fact]
    public void MessagePacket_SetsAddresseeAndText()
    {
        MessagePacket pkt = new()
        {
            Source = Source,
            Destination = Dest,
            Raw = ":N0CALL   :Hello there",
            Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL")),
            Text = "Hello there"
        };

        Assert.IsType<MessagePacket>(pkt);
        Assert.Equal(AprsAddress.FromCallsign(new Callsign("N0CALL")), pkt.Addressee);
        Assert.Equal("Hello there", pkt.Text);
        Assert.Null(pkt.MessageNumber);
    }

    [Fact]
    public void MessagePacket_WithMessageNumber_SetsNumber()
    {
        MessagePacket pkt = new()
        {
            Source = Source,
            Destination = Dest,
            Raw = ":N0CALL   :Hello{5}",
            Addressee = AprsAddress.FromCallsign(new Callsign("N0CALL")),
            Text = "Hello",
            MessageNumber = 5
        };

        Assert.Equal(5, pkt.MessageNumber);
        Assert.Equal("Hello", pkt.Text);
    }

    [Fact]
    public void StatusPacket_SetsText()
    {
        StatusPacket pkt = new()
        {
            Source = Source,
            Destination = Dest,
            Raw = ">Online via APRS",
            Text = "Online via APRS"
        };

        Assert.IsType<StatusPacket>(pkt);
        Assert.Equal("Online via APRS", pkt.Text);
    }

    [Fact]
    public void WeatherPacket_DefaultAllNull()
    {
        WeatherPacket pkt = new()
        {
            Source = Source,
            Destination = Dest,
            Raw = "_"
        };

        Assert.IsType<WeatherPacket>(pkt);
        Assert.Null(pkt.WindDirection);
        Assert.Null(pkt.WindSpeed);
        Assert.Null(pkt.Temperature);
        Assert.Null(pkt.Humidity);
        Assert.Null(pkt.Pressure);
    }

    [Fact]
    public void WeatherPacket_WithValues_SetsFields()
    {
        WeatherPacket pkt = new()
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
        UnknownPacket pkt = new()
        {
            Source = Source,
            Destination = Dest,
            Raw = "some unknown data"
        };

        Assert.IsType<UnknownPacket>(pkt);
        Assert.Equal("some unknown data", pkt.Raw);
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;
using AetherAprs.Models.Aprs;
using AetherAprs.Modems.Aprs;
using Xunit;

namespace AetherAprs.Tests.Aprs;

public class AprsParserTests
{
    // ---------------------------------------------------------------
    // Position parsing
    // ---------------------------------------------------------------

    [Fact]
    public void ParseInfoField_PositionNoTimestamp_ReturnsPositionPacket()
    {
        // "!" = position without timestamp
        // "!3830.00N/00906.00E#Test" = 38°30.00'N = 38.5, 9°06.00'E = 9.1
        var result = AprsParser.ParseInfoField("!3830.00N/00906.00E#Test",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(38.5, pos.Latitude, 6);
        Assert.Equal(9.1, pos.Longitude, 6);
        Assert.Equal('/', pos.Symbol.Table);
        Assert.Equal('#', pos.Symbol.Code);
        Assert.Equal("Test", pos.Comment);
        Assert.Equal(2, pos.Precision);
    }

    [Fact]
    public void ParseInfoField_PositionWithTimestamp_ReturnsPositionPacket()
    {
        // "@" = position with timestamp
        var result = AprsParser.ParseInfoField("@123456z3830.00N/00906.00E#Test",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(38.5, pos.Latitude, 6);
        Assert.Equal(9.1, pos.Longitude, 6);
    }

    [Fact]
    public void ParseInfoField_PositionSinglePrecision_ReturnsPositionPacket()
    {
        var result = AprsParser.ParseInfoField("=3830.0N/00906.0E>",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(38.5, pos.Latitude, 6);
        Assert.Equal(9.1, pos.Longitude, 6);
        Assert.Equal(1, pos.Precision);
    }

    [Fact]
    public void ParseInfoField_PositionNoComment_ReturnsPositionPacket()
    {
        var result = AprsParser.ParseInfoField("!3830.00N/00906.00E#",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(38.5, pos.Latitude, 6);
        Assert.Equal(9.1, pos.Longitude, 6);
        Assert.Null(pos.Comment);
    }

    [Fact]
    public void ParseInfoField_PositionSouthernHemisphere_LatitudeNegative()
    {
        var result = AprsParser.ParseInfoField("!3830.00S/00906.00W#",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(-38.5, pos.Latitude, 6);
    }

    [Fact]
    public void ParseInfoField_PositionEasternHemisphere_LongitudePositive()
    {
        var result = AprsParser.ParseInfoField("!3830.00N/00906.00E#",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(9.1, pos.Longitude, 6);
    }

    // ---------------------------------------------------------------
    // Message parsing
    // ---------------------------------------------------------------

    [Fact]
    public void ParseInfoField_Message_ReturnsMessagePacket()
    {
        var result = AprsParser.ParseInfoField(":N0CALL   :Hello there",
            new Callsign("MYCALL"), new Callsign("APZ001"));

        var msg = Assert.IsType<MessagePacket>(result);
        Assert.Equal(new Callsign("N0CALL"), msg.Addressee);
        Assert.Equal("Hello there", msg.Text);
        Assert.Null(msg.MessageNumber);
    }

    [Fact]
    public void ParseInfoField_MessageWithNumber_ReturnsMessageWithNumber()
    {
        var result = AprsParser.ParseInfoField(":N0CALL   :Hello{5}",
            new Callsign("MYCALL"), new Callsign("APZ001"));

        var msg = Assert.IsType<MessagePacket>(result);
        Assert.Equal("Hello", msg.Text);
        Assert.Equal(5, msg.MessageNumber);
    }

    [Fact]
    public void ParseInfoField_MessageShortAddressee_PadsCorrectly()
    {
        var result = AprsParser.ParseInfoField(":N0CALL:Hi",
            new Callsign("MYCALL"), new Callsign("APZ001"));

        var msg = Assert.IsType<MessagePacket>(result);
        // The addressee is trimmed from "N0CALL   " (9 chars)
        Assert.Equal(new Callsign("N0CALL"), msg.Addressee);
        Assert.Equal("Hi", msg.Text);
    }

    // ---------------------------------------------------------------
    // Status parsing
    // ---------------------------------------------------------------

    [Fact]
    public void ParseInfoField_Status_ReturnsStatusPacket()
    {
        var result = AprsParser.ParseInfoField(">Online via APRS",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var status = Assert.IsType<StatusPacket>(result);
        Assert.Equal("Online via APRS", status.Text);
    }

    [Fact]
    public void ParseInfoField_StatusEmpty_ReturnsEmptyText()
    {
        var result = AprsParser.ParseInfoField(">",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var status = Assert.IsType<StatusPacket>(result);
        Assert.Equal(string.Empty, status.Text);
    }

    // ---------------------------------------------------------------
    // Weather parsing
    // ---------------------------------------------------------------

    [Fact]
    public void ParseInfoField_Weather_ReturnsWeatherPacket()
    {
        var result = AprsParser.ParseInfoField("_c100s020g030t080h55b10100",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var w = Assert.IsType<WeatherPacket>(result);
        Assert.Equal(100, w.WindDirection);
        Assert.Equal(20, w.WindSpeed);
        Assert.Equal(30, w.WindGust);
        Assert.Equal(80, w.Temperature);
        Assert.Equal(55, w.Humidity);
        Assert.Equal(10100, w.Pressure);
    }

    [Fact]
    public void ParseInfoField_WeatherPartial_OnlyParsesPresentFields()
    {
        var result = AprsParser.ParseInfoField("_c090s015b10150",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var w = Assert.IsType<WeatherPacket>(result);
        Assert.Equal(90, w.WindDirection);
        Assert.Equal(15, w.WindSpeed);
        Assert.Equal(10150, w.Pressure);
        Assert.Null(w.Temperature);
        Assert.Null(w.Humidity);
        Assert.Null(w.Rain1h);
    }

    [Fact]
    public void ParseInfoField_WeatherEmpty_ReturnsEmptyWeather()
    {
        var result = AprsParser.ParseInfoField("_",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var w = Assert.IsType<WeatherPacket>(result);
        Assert.Null(w.WindDirection);
    }

    // ---------------------------------------------------------------
    // Unknown parsing
    // ---------------------------------------------------------------

    [Fact]
    public void ParseInfoField_UnknownType_ReturnsUnknownPacket()
    {
        var result = AprsParser.ParseInfoField("$some weird data",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        Assert.IsType<UnknownPacket>(result);
    }

    [Fact]
    public void ParseInfoField_NullOrEmpty_ReturnsUnknownPacket()
    {
        var result = AprsParser.ParseInfoField(string.Empty,
            new Callsign("N0CALL"), new Callsign("APZ001"));

        Assert.IsType<UnknownPacket>(result);
    }

    // ---------------------------------------------------------------
    // AX.25 frame parsing
    // ---------------------------------------------------------------

    [Fact]
    public void ParseFrame_ValidAx25Position_ReturnsPositionPacket()
    {
        var frame = BuildAx25Frame("APZ001", "N0CALL", "!3830.00N/00906.00E#");
        var result = AprsParser.ParseFrame(frame);

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(new Callsign("N0CALL"), pos.Source);
        Assert.Equal(new Callsign("APZ001"), pos.Destination);
        Assert.Equal(38.5, pos.Latitude, 6);
        Assert.Equal(9.1, pos.Longitude, 6);
    }

    [Fact]
    public void ParseFrame_ValidAx25Message_ReturnsMessagePacket()
    {
        var frame = BuildAx25Frame("APZ001", "N0CALL", ":OTHER    :Hello!");
        var result = AprsParser.ParseFrame(frame);

        var msg = Assert.IsType<MessagePacket>(result);
        Assert.Equal(new Callsign("N0CALL"), msg.Source);
        Assert.Equal(new Callsign("OTHER"), msg.Addressee);
        Assert.Equal("Hello!", msg.Text);
    }

    [Fact]
    public void ParseFrame_TrailingBytesInAddresses_DoesNotThrow()
    {
        // Simulate a frame with a digipeater address (3 addresses)
        var frame = BuildAx25Frame("APZ001", "N0CALL", ["WIDE1-1"], "!3830.00N/00906.00E#");
        var result = AprsParser.ParseFrame(frame);

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(38.5, pos.Latitude, 6);
    }

    [Fact]
    public void ParseFrame_TooShort_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => AprsParser.ParseFrame([0x01, 0x02]));
        Assert.Contains("too short", ex.Message);
    }

    [Fact]
    public void ParseFrame_EmptyInfo_ReturnsUnknownPacket()
    {
        var frame = BuildAx25Frame("APZ001", "N0CALL", "");
        var result = AprsParser.ParseFrame(frame);

        Assert.IsType<UnknownPacket>(result);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static byte[] BuildAx25Frame(string destination, string source, string infoField)
    {
        return BuildAx25Frame(destination, source, [], infoField);
    }

    private static byte[] BuildAx25Frame(string destination, string source, string[] digipeaters, string infoField)
    {
        var frames = new List<byte>();
        bool hasDigipeaters = digipeaters.Length > 0;

        // Destination address — never last when source follows
        frames.AddRange(EncodeAddress(destination, 0x60));

        // Source — last only if no digipeaters follow
        byte sourceSsid = hasDigipeaters ? (byte)0x60 : (byte)0x61;
        frames.AddRange(EncodeAddress(source, sourceSsid));

        // Digipeater addresses — last one has bit 0 = 1
        for (int i = 0; i < digipeaters.Length; i++)
        {
            bool isLast = i == digipeaters.Length - 1;
            byte digiSsid = isLast ? (byte)0x61 : (byte)0x60;
            frames.AddRange(EncodeAddress(digipeaters[i], digiSsid));
        }

        // Control: UI-frame
        frames.Add(0x03);

        // PID: no layer 3
        frames.Add(0xF0);

        // Info field
        frames.AddRange(Encoding.ASCII.GetBytes(infoField));

        return [.. frames];
    }

    private static byte[] EncodeAddress(string callsign, byte ssidByte)
    {
        var bytes = new byte[7];

        // Parse callsign base and SSID
        string @base = callsign;
        if (callsign.Contains('-'))
        {
            @base = callsign.Split('-')[0];
        }

        // Pad to 6 chars
        @base = @base.PadRight(6, ' ');

        for (int i = 0; i < 6; i++)
        {
            bytes[i] = (byte)(@base[i] << 1);
        }

        bytes[6] = ssidByte;
        return bytes;
    }
}
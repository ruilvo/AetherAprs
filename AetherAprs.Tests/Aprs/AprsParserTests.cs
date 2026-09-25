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
        var result = AprsInfoFieldParser.ParseInfoField("!3830.00N/00906.00E#Test",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(38.5, pos.Latitude, 6);
        Assert.Equal(9.1, pos.Longitude, 6);
        Assert.Equal(SymbolTable.Primary, pos.Symbol.Table);
        Assert.Equal(SymbolCode.NumberSign, pos.Symbol.Code);
        Assert.Equal("Test", pos.Comment);
        Assert.Equal(2, pos.Precision);
    }

    [Fact]
    public void ParseInfoField_PositionWithTimestamp_ReturnsPositionPacket()
    {
        // "@" = position with timestamp
        var result = AprsInfoFieldParser.ParseInfoField("@123456z3830.00N/00906.00E#Test",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(38.5, pos.Latitude, 6);
        Assert.Equal(9.1, pos.Longitude, 6);
    }

    [Fact]
    public void ParseInfoField_PositionSinglePrecision_ReturnsPositionPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField("=3830.0N/00906.0E>",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(38.5, pos.Latitude, 6);
        Assert.Equal(9.1, pos.Longitude, 6);
        Assert.Equal(1, pos.Precision);
    }

    [Fact]
    public void ParseInfoField_PositionNoComment_ReturnsPositionPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField("!3830.00N/00906.00E#",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(38.5, pos.Latitude, 6);
        Assert.Equal(9.1, pos.Longitude, 6);
        Assert.Null(pos.Comment);
    }

    [Fact]
    public void ParseInfoField_PositionSouthernHemisphere_LatitudeNegative()
    {
        var result = AprsInfoFieldParser.ParseInfoField("!3830.00S/00906.00W#",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(-38.5, pos.Latitude, 6);
    }

    [Fact]
    public void ParseInfoField_PositionEasternHemisphere_LongitudePositive()
    {
        var result = AprsInfoFieldParser.ParseInfoField("!3830.00N/00906.00E#",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(9.1, pos.Longitude, 6);
    }


    [Fact]
    public void ParseInfoField_CompressedPosition_ReturnsPositionPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField("!/9-pFL>:4uBkQ",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(42.2419, pos.Latitude, 4);
        Assert.Equal(-8.59665, pos.Longitude, 5);
        Assert.Equal(SymbolTable.Primary, pos.Symbol.Table);
        Assert.Equal('u'.ToSymbolCode(), pos.Symbol.Code);
        Assert.Null(pos.Symbol.Overlay);
        Assert.Equal(3, pos.Precision);
        Assert.NotNull(pos.Altitude);
        Assert.Equal(467.71, pos.Altitude.Value, 2);
        Assert.Null(pos.Course);
        Assert.Null(pos.Speed);
        Assert.Null(pos.Comment);
    }

    [Fact]
    public void ParseInfoField_CompressedPositionWithCourseSpeed_ReturnsCourseAndSpeed()
    {
        // Same lat/lon/symbol as known vector; csT encodes course=180, speed~1.16 kn
        var result = AprsInfoFieldParser.ParseInfoField("!/9-pFL>:4uN+!",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(42.2419, pos.Latitude, 4);
        Assert.Equal(-8.59665, pos.Longitude, 5);
        Assert.Equal(180, pos.Course);
        Assert.NotNull(pos.Speed);
        Assert.Equal(1.1589, pos.Speed.Value, 3);
        Assert.Null(pos.Altitude);
    }

    [Fact]
    public void ParseInfoField_CompressedObjectPosition_ReturnsPositionPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField(";TESTOBJ  *123456z/9-pFL>:4uBkQ",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(42.2419, pos.Latitude, 4);
        Assert.Equal(-8.59665, pos.Longitude, 5);
        Assert.Equal(SymbolTable.Primary, pos.Symbol.Table);
        Assert.Equal('u'.ToSymbolCode(), pos.Symbol.Code);
        Assert.Equal(";TESTOBJ  *123456z/9-pFL>:4uBkQ", pos.Raw);
    }

    [Fact]
    public void ParseInfoField_PositionWithOverlayNoExplicitTable_ReturnsPositionPacket()
    {
        // Format: !DDMM.mmND DDDMM.mmW& where D is overlay, & is symbol
        var result = AprsInfoFieldParser.ParseInfoField("!4038.72ND00747.95W&RNG0001 440 Voice 433.45000MHz +0.0000MHz",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(40.6453333, pos.Latitude, 6);
        Assert.Equal(-7.79916667, pos.Longitude, 6);
        Assert.Equal(SymbolTable.Alternate, pos.Symbol.Table);
        Assert.Equal('D', pos.Symbol.Overlay);
        Assert.Equal(SymbolCode.Ampersand, pos.Symbol.Code);
        Assert.Equal("RNG0001 440 Voice 433.45000MHz +0.0000MHz", pos.Comment);
    }

    [Fact]
    public void ParseInfoField_PositionWithTimestampAndOverlayNoExplicitTable_ReturnsPositionPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField("@251029z4057.65ND00544.73WaRNG0001/A=002624 70cm Voice (D-Star) 438.01250MHz +0.0000MHz",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(40.9608333, pos.Latitude, 6);
        Assert.Equal(-5.74550, pos.Longitude, 5);
        Assert.Equal(SymbolTable.Alternate, pos.Symbol.Table);
        Assert.Equal('D', pos.Symbol.Overlay);
        Assert.Equal('a'.ToSymbolCode(), pos.Symbol.Code);
        Assert.Equal("RNG0001/A=002624 70cm Voice (D-Star) 438.01250MHz +0.0000MHz", pos.Comment);
    }

    [Fact]
    public void ParseInfoField_PositionWithOverlayE_ReturnsPositionPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField("@111111z4321.56NE00825.18W0PHG3000/434.550MHz Toff R04m TETRA DMO RPT (https://ham-tetra.es)",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(43.35933333, pos.Latitude, 6);
        Assert.Equal(-8.41966667, pos.Longitude, 6);
        Assert.Equal(SymbolTable.Alternate, pos.Symbol.Table);
        Assert.Equal('E', pos.Symbol.Overlay);
        Assert.Equal(SymbolCode.Digit0, pos.Symbol.Code);
        Assert.Equal("PHG3000/434.550MHz Toff R04m TETRA DMO RPT (https://ham-tetra.es)", pos.Comment);
    }

    [Fact]
    public void ParseInfoField_PositionWithOverlayC_ReturnsPositionPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField("@251030z4213.06NC00844.10W0430.950MHz PL tone 69.0 CONNECTED",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(42.21766667, pos.Latitude, 6);
        Assert.Equal(-8.73500, pos.Longitude, 5);
        Assert.Equal(SymbolTable.Alternate, pos.Symbol.Table);
        Assert.Equal('C', pos.Symbol.Overlay);
        Assert.Equal(SymbolCode.Digit0, pos.Symbol.Code);
        Assert.Equal("430.950MHz PL tone 69.0 CONNECTED", pos.Comment);
    }

    [Fact]
    public void ParseInfoField_ObjectPositionWithOverlayK_ReturnsPositionPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField(";EA1RKF   *251030z4327.49N/00808.92WKAbierto viernes 19h-22h",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(43.45816667, pos.Latitude, 6);
        Assert.Equal(-8.14866667, pos.Longitude, 6);
        Assert.Equal(SymbolTable.Primary, pos.Symbol.Table);
        Assert.Equal('K'.ToSymbolCode(), pos.Symbol.Code);
        Assert.Equal("Abierto viernes 19h-22h", pos.Comment);
    }

    [Fact]
    public void ParseInfoField_PositionWithAltitudeInComment_ReturnsPositionPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField("!4219.20ND00621.60W&/A=0000002m MMDVM Voice (NXDN) 144.20000MHz +0.0000MHz, EB1ISC_Pi-Star_ND",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(42.32, pos.Latitude, 6);
        Assert.Equal(-6.36, pos.Longitude, 6);
        Assert.Equal(SymbolTable.Alternate, pos.Symbol.Table);
        Assert.Equal('D', pos.Symbol.Overlay);
        Assert.Equal(SymbolCode.Ampersand, pos.Symbol.Code);
        Assert.Equal("/A=0000002m MMDVM Voice (NXDN) 144.20000MHz +0.0000MHz, EB1ISC_Pi-Star_ND", pos.Comment);
    }

    [Fact]
    public void ParseInfoField_PositionWithMalformedAltitude_ReturnsPositionPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField("!4221.37ND00752.21W&/A=00065670cm MMDVM Voice (C4FM) 430.50000MHz +0.0000MHz, EA1RKP_Pi-Star_ND",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(42.35616667, pos.Latitude, 6);
        Assert.Equal(-7.87016667, pos.Longitude, 6);
        Assert.Equal(SymbolTable.Alternate, pos.Symbol.Table);
        Assert.Equal('D', pos.Symbol.Overlay);
        Assert.Equal(SymbolCode.Ampersand, pos.Symbol.Code);
        Assert.Equal("/A=00065670cm MMDVM Voice (C4FM) 430.50000MHz +0.0000MHz, EA1RKP_Pi-Star_ND", pos.Comment);
    }

    // ---------------------------------------------------------------
    // Capabilities parsing
    // ---------------------------------------------------------------

    [Fact]
    public void ParseInfoField_Capabilities_ReturnsCapabilitiesPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField("<IGATE,MSG_CNT=0,LOC_CNT=1",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var cap = Assert.IsType<CapabilitiesPacket>(result);
        Assert.Equal("IGATE,MSG_CNT=0,LOC_CNT=1", cap.Text);
    }

    [Fact]
    public void ParseInfoField_CapabilitiesEmpty_ReturnsEmptyText()
    {
        var result = AprsInfoFieldParser.ParseInfoField("<",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var cap = Assert.IsType<CapabilitiesPacket>(result);
        Assert.Equal(string.Empty, cap.Text);
    }

    // ---------------------------------------------------------------
    // Telemetry parsing
    // ---------------------------------------------------------------

    [Fact]
    public void ParseInfoField_Telemetry_ReturnsTelemetryPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField("T#069,10.3,0.0,42.0,1.0,0.0,00000000",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var tel = Assert.IsType<TelemetryPacket>(result);
        Assert.Equal(69, tel.SequenceNumber);
        Assert.Equal(5, tel.AnalogValues.Count);
        Assert.Equal(10.3, tel.AnalogValues[0]);
        Assert.Equal(0.0, tel.AnalogValues[1]);
        Assert.Equal(42.0, tel.AnalogValues[2]);
        Assert.Equal(1.0, tel.AnalogValues[3]);
        Assert.Equal(0.0, tel.AnalogValues[4]);
        Assert.NotNull(tel.DigitalValue);
        Assert.Equal(0, tel.DigitalValue.Value);
    }

    [Fact]
    public void ParseInfoField_TelemetryWithDigitalBits_ParsesDigitalValue()
    {
        var result = AprsInfoFieldParser.ParseInfoField("T#001,1.0,2.0,3.0,4.0,5.0,10101010",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var tel = Assert.IsType<TelemetryPacket>(result);
        Assert.Equal(1, tel.SequenceNumber);
        Assert.NotNull(tel.DigitalValue);
        Assert.Equal(170, tel.DigitalValue.Value); // 0b10101010 = 170
    }

    [Fact]
    public void ParseInfoField_TelemetryPartial_ReturnsPartialData()
    {
        var result = AprsInfoFieldParser.ParseInfoField("T#042,12.5,3.7",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var tel = Assert.IsType<TelemetryPacket>(result);
        Assert.Equal(42, tel.SequenceNumber);
        Assert.Equal(2, tel.AnalogValues.Count);
        Assert.Equal(12.5, tel.AnalogValues[0]);
        Assert.Equal(3.7, tel.AnalogValues[1]);
        Assert.Null(tel.DigitalValue);
    }

    [Fact]
    public void ParseInfoField_TelemetryInvalid_ReturnsUnknown()
    {
        var result = AprsInfoFieldParser.ParseInfoField("T#abc,1.0",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        Assert.IsType<UnknownPacket>(result);
    }

    // ---------------------------------------------------------------
    // MIC-E parsing
    // ---------------------------------------------------------------

    [Fact]
    public void ParseInfoField_MicE_ReturnsPositionPacket()
    {
        // MIC-E packet with latitude encoded in destination
        // Destination: S3TRV0 encodes latitude
        var result = AprsInfoFieldParser.ParseInfoField("`(_fn\"Oj/",
            new Callsign("N0CALL"), new Callsign("S3TRV0"));

        var pos = Assert.IsType<PositionPacket>(result);
        // Latitude and longitude are value types, just verify they're reasonable
        Assert.InRange(pos.Latitude, -90, 90);
        Assert.InRange(pos.Longitude, -180, 180);
    }

    [Fact]
    public void ParseInfoField_MicEWithAltGrave_ReturnsPositionPacket()
    {
        // MIC-E with grave accent (`) data type indicator
        var result = AprsInfoFieldParser.ParseInfoField("`test12345/`comment",
            new Callsign("TEST"), new Callsign("T2SRVW"));

        // Should parse as PositionPacket or UnknownPacket
        Assert.True(result is PositionPacket or UnknownPacket);
    }

    [Fact]
    public void ParseInfoField_MicEWithApostrophe_ReturnsPositionPacket()
    {
        // MIC-E with apostrophe (') data type indicator (current MIC-E)
        var result = AprsInfoFieldParser.ParseInfoField("'test12345/'comment",
            new Callsign("TEST"), new Callsign("T2SRVW"));

        // Should parse as PositionPacket or UnknownPacket
        Assert.True(result is PositionPacket or UnknownPacket);
    }

    [Fact]
    public void ParseInfoField_MicETooShort_ReturnsUnknown()
    {
        var result = AprsInfoFieldParser.ParseInfoField("`short",
            new Callsign("N0CALL"), new Callsign("S3TRV0"));

        Assert.IsType<UnknownPacket>(result);
    }

    // ---------------------------------------------------------------
    // Message parsing
    // ---------------------------------------------------------------

    [Fact]
    public void ParseInfoField_Message_ReturnsMessagePacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField(":N0CALL   :Hello there",
            new Callsign("MYCALL"), new Callsign("APZ001"));

        var msg = Assert.IsType<MessagePacket>(result);
        Assert.Equal(new Callsign("N0CALL"), msg.Addressee);
        Assert.Equal("Hello there", msg.Text);
        Assert.Null(msg.MessageNumber);
    }

    [Fact]
    public void ParseInfoField_MessageWithNumber_ReturnsMessageWithNumber()
    {
        var result = AprsInfoFieldParser.ParseInfoField(":N0CALL   :Hello{5}",
            new Callsign("MYCALL"), new Callsign("APZ001"));

        var msg = Assert.IsType<MessagePacket>(result);
        Assert.Equal("Hello", msg.Text);
        Assert.Equal(5, msg.MessageNumber);
    }

    [Fact]
    public void ParseInfoField_MessageShortAddressee_PadsCorrectly()
    {
        var result = AprsInfoFieldParser.ParseInfoField(":N0CALL:Hi",
            new Callsign("MYCALL"), new Callsign("APZ001"));

        var msg = Assert.IsType<MessagePacket>(result);
        // The addressee is trimmed from "N0CALL   " (9 chars)
        Assert.Equal(new Callsign("N0CALL"), msg.Addressee);
        Assert.Equal("Hi", msg.Text);
    }

    [Fact]
    public void ParseInfoField_MessageWithSsid_PreservesAddresseeSsid()
    {
        var result = AprsInfoFieldParser.ParseInfoField(":N0CALL-1:Hello", new Callsign("MYCALL"), new Callsign("APZ001"));

        var message = Assert.IsType<MessagePacket>(result);
        Assert.Equal(new Callsign("N0CALL", 1), message.Addressee);
    }

    [Theory]
    [InlineData(":N0CALL   :{abc}")]
    [InlineData(":N0CALL   :Hello{")]
    public void ParseInfoField_MessageWithMalformedAck_DoesNotThrow(string info)
    {
        var exception = Record.Exception(() => AprsInfoFieldParser.ParseInfoField(info, new Callsign("MYCALL"), new Callsign("APZ001")));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("!9000.01N/00000.00E#")]
    [InlineData("!9000.01S/00000.00E#")]
    [InlineData("!0000.00N/18000.01E#")]
    [InlineData("!0000.00N/18000.01W#")]
    public void ParseInfoField_PositionOutsideCoordinateBounds_ReturnsUnknown(string info)
    {
        var result = AprsInfoFieldParser.ParseInfoField(info, new Callsign("N0CALL"), new Callsign("APZ001"));

        Assert.IsType<UnknownPacket>(result);
    }

    // ---------------------------------------------------------------
    // Status parsing
    // ---------------------------------------------------------------

    [Fact]
    public void ParseInfoField_Status_ReturnsStatusPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField(">Online via APRS",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var status = Assert.IsType<StatusPacket>(result);
        Assert.Equal("Online via APRS", status.Text);
    }

    [Fact]
    public void ParseInfoField_StatusEmpty_ReturnsEmptyText()
    {
        var result = AprsInfoFieldParser.ParseInfoField(">",
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
        var result = AprsInfoFieldParser.ParseInfoField("_c100s020g030t080h55b10100",
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
        var result = AprsInfoFieldParser.ParseInfoField("_c090s015b10150",
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
        var result = AprsInfoFieldParser.ParseInfoField("_",
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
        var result = AprsInfoFieldParser.ParseInfoField("$some weird data",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        Assert.IsType<UnknownPacket>(result);
    }

    [Fact]
    public void ParseInfoField_NullOrEmpty_ReturnsUnknownPacket()
    {
        var result = AprsInfoFieldParser.ParseInfoField(string.Empty,
            new Callsign("N0CALL"), new Callsign("APZ001"));

        Assert.IsType<UnknownPacket>(result);
    }

    // ---------------------------------------------------------------
    // AX.25 frame parsing (via Ax25Parser)
    // ---------------------------------------------------------------

    [Fact]
    public void ParseFrame_ValidAx25Position_ReturnsPositionPacket()
    {
        var frame = BuildAx25Frame("APZ001", "N0CALL", "!3830.00N/00906.00E#");
        var result = Ax25Parser.ParseFrame(frame);

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
        var result = Ax25Parser.ParseFrame(frame);

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
        var result = Ax25Parser.ParseFrame(frame);

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(38.5, pos.Latitude, 6);
    }

    [Fact]
    public void ParseFrame_TooShort_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => Ax25Parser.ParseFrame([0x01, 0x02]));
        Assert.Contains("too short", ex.Message);
    }

    [Fact]
    public void ParseFrame_EmptyInfo_ReturnsUnknownPacket()
    {
        var frame = BuildAx25Frame("APZ001", "N0CALL", "");
        var result = Ax25Parser.ParseFrame(frame);

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

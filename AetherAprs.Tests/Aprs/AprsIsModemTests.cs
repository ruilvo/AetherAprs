// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;
using AetherAprs.Models.Aprs;
using AetherAprs.Modems.Aprs;
using Xunit;

namespace AetherAprs.Tests.Aprs;

public class AprsIsModemTests
{
    private static readonly Callsign Source = new("N0CALL");
    private static readonly Callsign Dest = new("APZ001");

    // ---------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------

    [Fact]
    public void Constructor_NullHost_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AprsIsModem(null!, 14580, Source, "12345"));
    }

    [Fact]
    public void Constructor_EmptyHost_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new AprsIsModem("", 14580, Source, "12345"));
    }

    [Fact]
    public void Constructor_ValidArgs_Succeeds()
    {
        var modem = new AprsIsModem("rotate.aprs2.net", 14580, Source, "12345");
        Assert.NotNull(modem);
        modem.DisposeAsync();
    }

    // ---------------------------------------------------------------
    // Start / Stop lifecycle
    // ---------------------------------------------------------------

    [Fact]
    public async Task Start_FirstCall_Succeeds()
    {
        await using var modem = new AprsIsModem("localhost", 14580, Source, "12345");
        modem.Start();
        // No exception is the assertion
    }

    [Fact]
    public async Task Start_DoubleCall_ThrowsInvalidOperationException()
    {
        await using var modem = new AprsIsModem("localhost", 14580, Source, "12345");
        modem.Start();
        Assert.Throws<InvalidOperationException>(() => modem.Start());
    }

    [Fact]
    public async Task StopAsync_WithoutStart_IsNoOp()
    {
        await using var modem = new AprsIsModem("localhost", 14580, Source, "12345");
        await modem.StopAsync();
        // No exception is the assertion
    }

    [Fact]
    public async Task StartThenStopAsync_CompletesCleanly()
    {
        await using var modem = new AprsIsModem("localhost", 14580, Source, "12345");
        modem.Start();
        await modem.StopAsync();
    }

    // ---------------------------------------------------------------
    // SendAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task SendAsync_NotStarted_ThrowsInvalidOperationException()
    {
        await using var modem = new AprsIsModem("localhost", 14580, Source, "12345");

        var packet = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Latitude = 38.5,
            Longitude = -9.10,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Precision = 2
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            modem.SendAsync(packet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_NullPacket_ThrowsArgumentNullException()
    {
        await using var modem = new AprsIsModem("localhost", 14580, Source, "12345");

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            modem.SendAsync(null!, TestContext.Current.CancellationToken));
    }

    // ---------------------------------------------------------------
    // APRS-IS line parsing
    // ---------------------------------------------------------------

    [Fact]
    public void ParseIsLine_PositionPacket_ReturnsPositionPacket()
    {
        var line = "N0CALL>APZ001:!3830.00N/00906.00E#Test";
        var result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(new Callsign("N0CALL"), pos.Source);
        Assert.Equal(new Callsign("APZ001"), pos.Destination);
        Assert.Equal(38.5, pos.Latitude, 6);
        Assert.Equal(9.1, pos.Longitude, 6);
        Assert.Equal("Test", pos.Comment);
    }

    [Fact]
    public void ParseIsLine_MessagePacket_ReturnsMessagePacket()
    {
        var line = "N0CALL>APZ001::OTHER   :Hello there";
        var result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        var msg = Assert.IsType<MessagePacket>(result);
        Assert.Equal(new Callsign("N0CALL"), msg.Source);
        Assert.Equal(new Callsign("OTHER"), msg.Addressee);
        Assert.Equal("Hello there", msg.Text);
    }

    [Fact]
    public void ParseIsLine_StatusPacket_ReturnsStatusPacket()
    {
        var line = "N0CALL>APZ001:>Online via APRS";
        var result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        var status = Assert.IsType<StatusPacket>(result);
        Assert.Equal("Online via APRS", status.Text);
    }

    [Fact]
    public void ParseIsLine_WeatherPacket_ReturnsWeatherPacket()
    {
        var line = "N0CALL>APZ001:_c100s020g030t080h55b10100";
        var result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        var w = Assert.IsType<WeatherPacket>(result);
        Assert.Equal(100, w.WindDirection);
        Assert.Equal(20, w.WindSpeed);
        Assert.Equal(30, w.WindGust);
    }

    [Fact]
    public void ParseIsLine_WithDigipeaters_ParsesCorrectly()
    {
        var line = "N0CALL>APZ001,WIDE1-1,WIDE2-1:!3830.00N/00906.00E#";
        var result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(new Callsign("N0CALL"), pos.Source);
        Assert.Equal(new Callsign("APZ001"), pos.Destination);
    }

    [Fact]
    public void ParseIsLine_WithSSID_ParsesCorrectly()
    {
        var line = "N0CALL-1>APZ001-5:!3830.00N/00906.00E#";
        var result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(new Callsign("N0CALL", 1), pos.Source);
        Assert.Equal(new Callsign("APZ001", 5), pos.Destination);
    }

    [Fact]
    public void ParseIsLine_UnknownPacket_ReturnsUnknownPacket()
    {
        var line = "N0CALL>APZ001:$some weird data";
        var result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        Assert.IsType<UnknownPacket>(result);
    }

    [Fact]
    public void ParseIsLine_NoColon_ReturnsNull()
    {
        var line = "N0CALL>APZ001";
        var result = InvokeParseIsLine(line);

        Assert.Null(result);
    }

    [Fact]
    public void ParseIsLine_NoArrow_ReturnsNull()
    {
        var line = "N0CALL APZ001:!3830.00N/00906.00E#";
        var result = InvokeParseIsLine(line);

        Assert.Null(result);
    }

    [Fact]
    public void ParseIsLine_EmptyInfoField_ReturnsUnknownPacket()
    {
        var line = "N0CALL>APZ001:";
        var result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        Assert.IsType<UnknownPacket>(result);
    }

    [Fact]
    public void ParseIsLine_InvalidCallsign_ReturnsNull()
    {
        // Too-short base callsign (single char)
        var line = "X>APZ001:!3830.00N/00906.00E#";
        var result = InvokeParseIsLine(line);

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // Helper — invoke private static ParseIsLine via reflection
    // ---------------------------------------------------------------

    private static AprsPacket? InvokeParseIsLine(string line)
    {
        var method = typeof(AprsIsModem).GetMethod("ParseIsLine",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        if (method is null)
        {
            return null;
        }

        return method.Invoke(null, [line]) as AprsPacket;
    }
}
// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
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

    [Fact]
    public async Task SendAsync_AfterVerifiedLogin_UsesTcpIpPath()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using var modem = new AprsIsModem("127.0.0.1", endpoint.Port, Source, "12345", "m/50");

        modem.Start();
        using var server = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using var stream = server.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n"
        };

        var login = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.Contains("filter", login);

        await writer.WriteLineAsync("# logresp N0CALL verified, server TEST");
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        var packet = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Latitude = 38.5,
            Longitude = -9.10,
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Precision = 2
        };

        await modem.SendAsync(packet, TestContext.Current.CancellationToken);

        var transmitted = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("N0CALL>APZ001,TCPIP*:", transmitted);
        Assert.Contains("!3830.00N/00906.00W#", transmitted);
    }

    [Fact]
    public async Task Login_IncludesConfiguredSsid()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using var modem = new AprsIsModem(
            "127.0.0.1",
            endpoint.Port,
            new Callsign("N0CALL", 1),
            "12345");

        modem.Start();
        using var server = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using var stream = server.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        var login = await reader.ReadLineAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("user N0CALL-1 pass 12345", login);
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

    [Fact]
    public void ParseIsLine_ExtendedSourceIdentifier_PreservesSourceAndDecodesPacket()
    {
        var line = "7000tgi9>APHPIB,TCPIP*,qAC,T2CAEAST:;7000tgi9 *290819z0019.85S\\12017.83E!Earthquake";
        var result = InvokeParseIsLine(line);

        var position = Assert.IsType<PositionPacket>(result);
        Assert.Equal(new Callsign("APRS"), position.Source);
        Assert.Equal("7000TGI9", position.RawSource);
        Assert.Equal(-0.330833, position.Latitude, 5);
        Assert.Equal(120.297167, position.Longitude, 5);
        Assert.Equal("Earthquake", position.Comment);
    }

    [Fact]
    public void ParseIsLine_AlphaNumericSsid_PreservesSourceAndDecodesPacket()
    {
        var line = "4K0WHX-N0>APWHXF:>2349-507H-VGF3-0395-WJ2F-RN0F-K0TJA.";
        var result = InvokeParseIsLine(line);

        var status = Assert.IsType<StatusPacket>(result);
        Assert.Equal(new Callsign("APRS"), status.Source);
        Assert.Equal("4K0WHX-N0", status.RawSource);
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

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Aprs;
using Geo;
using Xunit;

namespace AetherAprs.Tests.Modems.Aprs;

public class AprsIsModemTests
{
    private static readonly Callsign Source = new("N0CALL");
    private static readonly Callsign Dest = new("APZ001");

    // ---------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------

    [Fact]
    public void Constructor_NullHost_ThrowsArgumentNullException() => Assert.Throws<ArgumentNullException>(() =>
                                                                               new AprsIsModem(null!, 14580, Source, "12345"));

    [Fact]
    public void Constructor_EmptyHost_ThrowsArgumentException() => Assert.Throws<ArgumentException>(() =>
                                                                            new AprsIsModem("", 14580, Source, "12345"));

    [Fact]
    public async Task Constructor_ValidArgs_Succeeds()
    {
        await using AprsIsModem modem = new("rotate.aprs2.net", 14580, Source, "12345");
        Assert.NotNull(modem);
    }

    // ---------------------------------------------------------------
    // Start / Stop lifecycle
    // ---------------------------------------------------------------

    [Fact]
    public async Task Start_FirstCall_Succeeds()
    {
        await using AprsIsModem modem = new("localhost", 14580, Source, "12345");
        modem.Start();
        // No exception is the assertion
    }

    [Fact]
    public async Task Start_DoubleCall_ThrowsInvalidOperationException()
    {
        await using AprsIsModem modem = new("localhost", 14580, Source, "12345");
        modem.Start();
        Assert.Throws<InvalidOperationException>(() => modem.Start());
    }

    [Fact]
    public async Task Start_ConcurrentCalls_OnlyOneSucceeds()
    {
        await using AprsIsModem modem = new("localhost", 14580, Source, "12345");

        const int contenders = 8;
        int started = 0;
        int failures = 0;

        Thread[] threads = [.. Enumerable.Range(0, contenders)
            .Select(_ => new Thread(() =>
            {
                try
                {
                    modem.Start();
                    Interlocked.Increment(ref started);
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref failures);
                }
            }))];

        foreach (Thread? thread in threads)
        {
            thread.Start();
        }

        foreach (Thread? thread in threads)
        {
            thread.Join();
        }

        Assert.Equal(1, started);
        Assert.Equal(contenders - 1, failures);

        await modem.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WithoutStart_IsNoOp()
    {
        await using AprsIsModem modem = new("localhost", 14580, Source, "12345");
        await modem.StopAsync();
        // No exception is the assertion
    }

    [Fact]
    public async Task StartThenStopAsync_CompletesCleanly()
    {
        await using AprsIsModem modem = new("localhost", 14580, Source, "12345");
        modem.Start();
        await modem.StopAsync();
    }

    // ---------------------------------------------------------------
    // SendAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task SendAsync_NotStarted_ThrowsInvalidOperationException()
    {
        await using AprsIsModem modem = new("localhost", 14580, Source, "12345");

        PositionPacket packet = new()
        {
            Source = Source,
            Destination = Dest,
            Location = new Coordinate(38.5, -9.10),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Precision = 2
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            modem.SendAsync(packet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_NullPacket_ThrowsArgumentNullException()
    {
        await using AprsIsModem modem = new("localhost", 14580, Source, "12345");

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            modem.SendAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_AfterVerifiedLogin_UsesQaoConstructor()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        IPEndPoint endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using AprsIsModem modem = new("127.0.0.1", endpoint.Port, Source, "12345", "m/50");

        modem.Start();
        using TcpClient server = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using NetworkStream stream = server.GetStream();
        using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        await using StreamWriter writer = new(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n"
        };

        string? login = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.Contains("filter", login);

        await writer.WriteLineAsync("# logresp N0CALL verified, server TEST");
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        PositionPacket packet = new()
        {
            Source = Source,
            Destination = Dest,
            Location = new Coordinate(38.5, -9.10),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Precision = 2
        };

        await modem.SendAsync(packet, TestContext.Current.CancellationToken);

        string? transmitted = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.Equal("N0CALL>APZ001,qAo:!3830.00N/00906.00W#", transmitted);
    }

    [Fact]
    public async Task SendAsync_BeforeLoginCompletes_WaitsForAuthenticationAndTransmits()
    {
        // Start() returns before the login response arrives. A send issued in that window
        // used to snapshot a null writer and throw "not connected" even though the
        // connection authenticated microseconds later.
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        IPEndPoint endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using AprsIsModem modem = new("127.0.0.1", endpoint.Port, Source, "12345", "m/50");

        modem.Start();
        using TcpClient server = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using NetworkStream stream = server.GetStream();
        using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        await using StreamWriter writer = new(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n"
        };

        _ = await reader.ReadLineAsync(TestContext.Current.CancellationToken);

        PositionPacket packet = new()
        {
            Source = Source,
            Destination = Dest,
            Location = new Coordinate(38.5, -9.10),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Precision = 2
        };

        Task send = modem.SendAsync(packet, TestContext.Current.CancellationToken);
        Assert.False(send.IsCompleted);

        await writer.WriteLineAsync("# logresp N0CALL verified, server TEST");
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        await send;

        string? transmitted = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.Equal("N0CALL>APZ001,qAo:!3830.00N/00906.00W#", transmitted);
    }

    [Fact]
    public async Task SendAsync_RfReceivedPacket_UsesQarConstructorWithPath()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        IPEndPoint endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using AprsIsModem modem = new("127.0.0.1", endpoint.Port, Source, "12345", "m/50");

        modem.Start();
        using TcpClient server = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using NetworkStream stream = server.GetStream();
        using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        await using StreamWriter writer = new(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n"
        };

        _ = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        await writer.WriteLineAsync("# logresp N0CALL verified, server TEST");
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Writing the reply only proves the bytes reached the socket. Awaiting the
        // modem's own readiness signal proves its read loop consumed the reply, so
        // the assertion below cannot race the authentication parser.
        await modem.ConnectionReady.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        // A packet received over RF carries a non-empty RawFrameHex, which tells
        // the modem to emit qAR (not qAo) and preserve the digipeater path.
        PositionPacket packet = new()
        {
            Source = Source,
            Destination = Dest,
            Location = new Coordinate(38.5, -9.10),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Precision = 2,
            RawFrameHex = "DEADBEEF",
            Path = [new Callsign("WIDE1", 1, hasBeenRepeated: true), new Callsign("WIDE2", 1)]
        };

        await modem.SendAsync(packet, TestContext.Current.CancellationToken);

        string? transmitted = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.Equal("N0CALL>APZ001,WIDE1-1*,WIDE2-1,qAR:!3830.00N/00906.00W#", transmitted);
    }

    [Fact]
    public async Task Login_IncludesConfiguredSsid()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        IPEndPoint endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using AprsIsModem modem = new(
            "127.0.0.1",
            endpoint.Port,
            new Callsign("N0CALL", 1),
            "12345");

        modem.Start();
        using TcpClient server = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using NetworkStream stream = server.GetStream();
        using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);

        string? login = await reader.ReadLineAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("user N0CALL-1 pass 12345", login);
    }

    // ---------------------------------------------------------------
    // Login result handling (logresp)
    // ---------------------------------------------------------------

    [Fact]
    public async Task SendAsync_AfterUnverifiedLogin_UsesQaoConstructor()
    {
        // "unverified" is a successful READ-ONLY login (invalid/missing
        // passcode): the modem must accept it and keep transmitting.
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        IPEndPoint endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using AprsIsModem modem = new("127.0.0.1", endpoint.Port, Source, "12345", "m/50");

        modem.Start();
        using TcpClient server = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using NetworkStream stream = server.GetStream();
        using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        await using StreamWriter writer = new(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n"
        };

        _ = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        await writer.WriteLineAsync("# logresp N0CALL unverified, server TEST");
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        PositionPacket packet = new()
        {
            Source = Source,
            Destination = Dest,
            Location = new Coordinate(38.5, -9.10),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Precision = 2
        };

        await modem.SendAsync(packet, TestContext.Current.CancellationToken);

        string? transmitted = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.Equal("N0CALL>APZ001,qAo:!3830.00N/00906.00W#", transmitted);
    }

    [Fact]
    public async Task Receive_AfterUnverifiedLogin_DeliversPackets()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        IPEndPoint endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using AprsIsModem modem = new("127.0.0.1", endpoint.Port, Source, "12345", "m/50");

        TaskCompletionSource<AprsPacket> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        modem.PacketReceived += (_, packet) => received.TrySetResult(packet);

        modem.Start();
        using TcpClient server = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using NetworkStream stream = server.GetStream();
        using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        await using StreamWriter writer = new(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n"
        };

        _ = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        await writer.WriteLineAsync("# logresp N0CALL unverified, server TEST");
        await writer.FlushAsync(TestContext.Current.CancellationToken);
        await writer.WriteLineAsync("N0CALL>APZ001:>Online via APRS");
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        AprsPacket packet = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        StatusPacket status = Assert.IsType<StatusPacket>(packet);
        Assert.Equal("Online via APRS", status.Text);
    }

    [Fact]
    public async Task SendAsync_AfterFailedLogin_Throws()
    {
        // "failed" is the only true rejection: the modem must not proceed.
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        IPEndPoint endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using AprsIsModem modem = new("127.0.0.1", endpoint.Port, Source, "12345", "m/50");

        modem.Start();
        using TcpClient server = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using NetworkStream stream = server.GetStream();
        using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        await using StreamWriter writer = new(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n"
        };

        _ = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        await writer.WriteLineAsync("# logresp N0CALL failed, server TEST");
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        PositionPacket packet = new()
        {
            Source = Source,
            Destination = Dest,
            Location = new Coordinate(38.5, -9.10),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Precision = 2
        };

        Exception ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            modem.SendAsync(packet, TestContext.Current.CancellationToken));
        Assert.Contains("rejected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectionReady_FailedLogin_FaultsWithRejection()
    {
        // The readiness signal is the only observable proof that the login reply was
        // consumed, so it must report a rejection instead of completing successfully.
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        IPEndPoint endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using AprsIsModem modem = new("127.0.0.1", endpoint.Port, Source, "12345");

        modem.Start();
        using TcpClient server = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using NetworkStream stream = server.GetStream();
        using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        await using StreamWriter writer = new(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n"
        };

        _ = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        await writer.WriteLineAsync("# logresp N0CALL failed, server TEST");
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            modem.ConnectionReady.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));

        Assert.Contains("rejected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------
    // APRS-IS line parsing
    // ---------------------------------------------------------------

    [Fact]
    public void ParseIsLine_PositionPacket_ReturnsPositionPacket()
    {
        string line = "N0CALL>APZ001:!3830.00N/00906.00E#Test";
        AprsPacket? result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        PositionPacket pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(new Callsign("N0CALL"), pos.Source);
        Assert.Equal(new Callsign("APZ001"), pos.Destination);
        Assert.Equal(38.5, pos.Location.Latitude, 6);
        Assert.Equal(9.1, pos.Location.Longitude, 6);
        Assert.Equal("Test", pos.Comment);
    }

    [Fact]
    public void ParseIsLine_MessagePacket_ReturnsMessagePacket()
    {
        string line = "N0CALL>APZ001::OTHER   :Hello there";
        AprsPacket? result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        MessagePacket msg = Assert.IsType<MessagePacket>(result);
        Assert.Equal(new Callsign("N0CALL"), msg.Source);
        Assert.Equal(AprsAddress.FromCallsign(new Callsign("OTHER")), msg.Addressee);
        Assert.Equal("Hello there", msg.Text);
    }

    [Fact]
    public void ParseIsLine_StatusPacket_ReturnsStatusPacket()
    {
        string line = "N0CALL>APZ001:>Online via APRS";
        AprsPacket? result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        StatusPacket status = Assert.IsType<StatusPacket>(result);
        Assert.Equal("Online via APRS", status.Text);
    }

    [Fact]
    public void ParseIsLine_WeatherPacket_ReturnsWeatherPacket()
    {
        string line = "N0CALL>APZ001:_c100s020g030t080h55b10100";
        AprsPacket? result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        WeatherPacket w = Assert.IsType<WeatherPacket>(result);
        Assert.Equal(100, w.WindDirection);
        Assert.Equal(20, w.WindSpeed);
        Assert.Equal(30, w.WindGust);
    }

    [Fact]
    public void ParseIsLine_WithDigipeaters_ParsesCorrectly()
    {
        string line = "N0CALL>APZ001,WIDE1-1,WIDE2-1:!3830.00N/00906.00E#";
        AprsPacket? result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        PositionPacket pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(new Callsign("N0CALL"), pos.Source);
        Assert.Equal(new Callsign("APZ001"), pos.Destination);
    }

    [Fact]
    public void ParseIsLine_SetsRawPath_ToPathPortionBeforeColon()
    {
        string line = "N0CALL>APZ001,WIDE1-1,WIDE2-1:!3830.00N/00906.00E#";
        AprsPacket? result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        PositionPacket pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal("N0CALL>APZ001,WIDE1-1,WIDE2-1", pos.RawPath);
    }

    [Fact]
    public void ParseIsLine_WithSSID_ParsesCorrectly()
    {
        string line = "N0CALL-1>APZ001-5:!3830.00N/00906.00E#";
        AprsPacket? result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        PositionPacket pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(new Callsign("N0CALL", 1), pos.Source);
        Assert.Equal(new Callsign("APZ001", 5), pos.Destination);
    }

    [Fact]
    public void ParseIsLine_UnknownPacket_ReturnsUnknownPacket()
    {
        string line = "N0CALL>APZ001:#some weird data";
        AprsPacket? result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        Assert.IsType<UnknownPacket>(result);
    }

    [Fact]
    public void ParseIsLine_NoColon_ReturnsNull()
    {
        string line = "N0CALL>APZ001";
        AprsPacket? result = InvokeParseIsLine(line);

        Assert.Null(result);
    }

    [Fact]
    public void ParseIsLine_NoArrow_ReturnsNull()
    {
        string line = "N0CALL APZ001:!3830.00N/00906.00E#";
        AprsPacket? result = InvokeParseIsLine(line);

        Assert.Null(result);
    }

    [Fact]
    public void ParseIsLine_EmptyInfoField_ReturnsUnknownPacket()
    {
        string line = "N0CALL>APZ001:";
        AprsPacket? result = InvokeParseIsLine(line);

        Assert.NotNull(result);
        Assert.IsType<UnknownPacket>(result);
    }

    [Fact]
    public void ParseIsLine_InvalidCallsign_ReturnsNull()
    {
        // Too-short base callsign (single char)
        string line = "X>APZ001:!3830.00N/00906.00E#";
        AprsPacket? result = InvokeParseIsLine(line);

        Assert.Null(result);
    }

    [Fact]
    public void ParseIsLine_ExtendedSourceIdentifier_PreservesSourceAndDecodesPacket()
    {
        string line = "7000tgi9>APHPIB,TCPIP*,qAC,T2CAEAST:;7000tgi9 *290819z0019.85S\\12017.83E!Earthquake";
        AprsPacket? result = InvokeParseIsLine(line);

        ObjectPacket obj = Assert.IsType<ObjectPacket>(result);
        Assert.Equal(new Callsign("APRS"), obj.Source);
        Assert.Equal("7000TGI9", obj.RawSource);
        Assert.Equal("7000tgi9", obj.Name);
        Assert.True(obj.IsLive);
        Assert.Equal("290819z", obj.RawTimestamp);
        Assert.Equal(-0.330833, obj.Location.Latitude, 5);
        Assert.Equal(120.297167, obj.Location.Longitude, 5);
        Assert.Equal("Earthquake", obj.Comment);
    }

    [Fact]
    public void ParseIsLine_AlphaNumericSsid_PreservesSourceAndDecodesPacket()
    {
        string line = "4K0WHX-N0>APWHXF:>2349-507H-VGF3-0395-WJ2F-RN0F-K0TJA.";
        AprsPacket? result = InvokeParseIsLine(line);

        StatusPacket status = Assert.IsType<StatusPacket>(result);
        Assert.Equal(new Callsign("APRS"), status.Source);
        Assert.Equal("4K0WHX-N0", status.RawSource);
    }

    [Fact]
    public void ParseIsLine_HopWithSsidAbove15_SkipsHopAndStillParsesPacket()
    {
        // SSID 99 exceeds the 0-15 range. The invalid hop must be skipped
        // (ArgumentException caught) instead of aborting the whole line parse.
        string line = "XX1XX-99>APRS,INVALID-99,WIDE1-1:!3830.00N/00906.00E#";
        AprsPacket? result = InvokeParseIsLine(line);

        PositionPacket position = Assert.IsType<PositionPacket>(result);
        Assert.Equal(new Callsign("APRS"), position.Source);
    }

    [Fact]
    public void ParseIsLine_QConstructorsInPath_SkippedFromPathList()
    {
        // qAR/qAo/qAS/qX (and qAC) are server-inserted APRS-IS markers,
        // not real digipeater hops — they must not pollute the parsed path.
        string line = "N0CALL>APRS,TCPIP*,qAC,WIDE1-1,qAR,RELAY:!3830.00N/00906.00E#";
        AprsPacket? result = InvokeParseIsLine(line);

        PositionPacket position = Assert.IsType<PositionPacket>(result);
        Assert.Equal(2, position.Path.Count);
        Assert.Equal(new Callsign("WIDE1", 1), position.Path[0]);
        Assert.Equal(new Callsign("RELAY"), position.Path[1]);
    }

    // ---------------------------------------------------------------
    // Helper — invoke private static ParseIsLine via reflection
    // ---------------------------------------------------------------

    private static AprsPacket? InvokeParseIsLine(string line) =>
        // ParseIsLine is now internal, accessible via InternalsVisibleTo
        AprsIsModem.ParseIsLine(line);
}

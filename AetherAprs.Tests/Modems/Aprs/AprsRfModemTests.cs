// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Aprs;
using AetherAprs.Modems.Kiss;

using Geo;

using Xunit;

namespace AetherAprs.Tests.Modems.Aprs;

public class AprsRfModemTests
{
    private static readonly Callsign Source = new("N0CALL");
    private static readonly Callsign Dest = new("APZ001");

    // ---------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------

    [Fact]
    public void Constructor_NullKissModem_ThrowsArgumentNullException() => Assert.Throws<ArgumentNullException>(() => new AprsRfModem(null!));

    // ---------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------

    [Fact]
    public async Task Start_FirstCall_Succeeds()
    {
        await using MemoryStream stream = new();
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        aprsModem.Start();
        // No exception is the assertion
    }

    [Fact]
    public async Task Start_DoubleCall_ThrowsInvalidOperationException()
    {
        await using MemoryStream stream = new();
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        aprsModem.Start();
        Assert.Throws<InvalidOperationException>(() => aprsModem.Start());
    }

    [Fact]
    public async Task StopAsync_WithoutStart_IsNoOp()
    {
        await using MemoryStream stream = new();
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        await aprsModem.StopAsync();
        // No exception is the assertion
    }

    [Fact]
    public async Task StartThenStopAsync_CompletesCleanly()
    {
        await using MemoryStream stream = new();
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        aprsModem.Start();
        await aprsModem.StopAsync();
    }

    // ---------------------------------------------------------------
    // SendAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task SendAsync_PositionPacket_WritesKissFrame()
    {
        await using MemoryStream stream = new();
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        PositionPacket packet = new()
        {
            Source = Source,
            Destination = Dest,
            Location = new Coordinate(38.5, -9.10),
            Symbol = new Symbol(SymbolTable.Primary, SymbolCode.NumberSign),
            Precision = 2
        };

        await aprsModem.SendAsync(packet, TestContext.Current.CancellationToken);

        byte[] written = stream.ToArray();

        // Should start with FEND and end with FEND
        Assert.Equal(0xC0, written[0]);
        Assert.Equal(0xC0, written[^1]);

        // Command byte should be 0x00 (DataFrame, port 0)
        Assert.Equal(0x00, written[1]);
    }

    [Fact]
    public async Task SendAsync_MessagePacket_WritesKissFrame()
    {
        await using MemoryStream stream = new();
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        MessagePacket packet = new()
        {
            Source = Source,
            Destination = Dest,
            Addressee = AprsAddress.FromCallsign(new Callsign("OTHER")),
            Text = "Hello"
        };

        await aprsModem.SendAsync(packet, TestContext.Current.CancellationToken);

        byte[] written = stream.ToArray();
        Assert.Equal(0xC0, written[0]);
        Assert.Equal(0x00, written[1]);
        Assert.Equal(0xC0, written[^1]);
    }

    [Fact]
    public async Task SendAsync_StatusPacket_WritesKissFrame()
    {
        await using MemoryStream stream = new();
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        StatusPacket packet = new()
        {
            Source = Source,
            Destination = Dest,
            Text = "Online"
        };

        await aprsModem.SendAsync(packet, TestContext.Current.CancellationToken);

        byte[] written = stream.ToArray();
        Assert.Equal(0xC0, written[0]);
        Assert.Equal(0x00, written[1]);
    }

    [Fact]
    public async Task SendAsync_NullPacket_ThrowsArgumentNullException()
    {
        await using MemoryStream stream = new();
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            aprsModem.SendAsync(null!, TestContext.Current.CancellationToken));
    }

    // ---------------------------------------------------------------
    // PacketReceived
    // ---------------------------------------------------------------

    [Fact]
    public async Task PacketReceived_PositionFrame_FiresEvent()
    {
        // Build AX.25 position frame wrapped in KISS
        byte[] ax25Data = BuildAx25Frame("APZ001", "N0CALL", "!3830.00N/00906.00E#");
        byte[] kissBuffer = BuildKissFrame(ax25Data);

        await using MemoryStream stream = new(kissBuffer);
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        TaskCompletionSource<AprsPacket> tcs = new();
        aprsModem.PacketReceived += (_, packet) => tcs.TrySetResult(packet);

        aprsModem.Start();

        AprsPacket result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        PositionPacket pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(38.5, pos.Location.Latitude, 4);
        Assert.Equal(9.10, pos.Location.Longitude, 4);
        Assert.Equal(new Callsign("N0CALL"), pos.Source);
    }

    [Fact]
    public async Task PacketReceived_MessageFrame_FiresEvent()
    {
        byte[] ax25Data = BuildAx25Frame("APZ001", "N0CALL", ":OTHER   :Hello{5}");
        byte[] kissBuffer = BuildKissFrame(ax25Data);

        await using MemoryStream stream = new(kissBuffer);
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        TaskCompletionSource<AprsPacket> tcs = new();
        aprsModem.PacketReceived += (_, packet) => tcs.TrySetResult(packet);

        aprsModem.Start();

        AprsPacket result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        MessagePacket msg = Assert.IsType<MessagePacket>(result);
        Assert.Equal("Hello", msg.Text);
        Assert.Equal(5, msg.MessageNumber);
    }

    [Fact]
    public async Task PacketReceived_StatusFrame_FiresEvent()
    {
        byte[] ax25Data = BuildAx25Frame("APZ001", "N0CALL", ">Online via APRS");
        byte[] kissBuffer = BuildKissFrame(ax25Data);

        await using MemoryStream stream = new(kissBuffer);
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        TaskCompletionSource<AprsPacket> tcs = new();
        aprsModem.PacketReceived += (_, packet) => tcs.TrySetResult(packet);

        aprsModem.Start();

        AprsPacket result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        StatusPacket status = Assert.IsType<StatusPacket>(result);
        Assert.Equal("Online via APRS", status.Text);
    }

    [Fact]
    public async Task PacketReceived_WeatherFrame_FiresEvent()
    {
        byte[] ax25Data = BuildAx25Frame("APZ001", "N0CALL", "_c100s020t080h55b10100");
        byte[] kissBuffer = BuildKissFrame(ax25Data);

        await using MemoryStream stream = new(kissBuffer);
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        TaskCompletionSource<AprsPacket> tcs = new();
        aprsModem.PacketReceived += (_, packet) => tcs.TrySetResult(packet);

        aprsModem.Start();

        AprsPacket result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        WeatherPacket w = Assert.IsType<WeatherPacket>(result);
        Assert.Equal(100, w.WindDirection);
        Assert.Equal(20, w.WindSpeed);
    }

    [Fact]
    public async Task PacketReceived_UnknownPacket_FiresEvent()
    {
        byte[] ax25Data = BuildAx25Frame("APZ001", "N0CALL", "#weird format data");
        byte[] kissBuffer = BuildKissFrame(ax25Data);

        await using MemoryStream stream = new(kissBuffer);
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        TaskCompletionSource<AprsPacket> tcs = new();
        aprsModem.PacketReceived += (_, packet) => tcs.TrySetResult(packet);

        aprsModem.Start();

        AprsPacket result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<UnknownPacket>(result);
    }

    [Fact]
    public async Task PacketReceived_NonDataFrame_DoesNotFireEvent()
    {
        // KISS TXDelay command frame (not a data frame)
        byte[] kissBuffer = BuildKissFrame(0x01, [0x05]);

        await using MemoryStream stream = new(kissBuffer);
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        bool packetReceived = false;
        aprsModem.PacketReceived += (_, _) => packetReceived = true;

        aprsModem.Start();

        // Negative test: wait to verify event does NOT fire.
        // Pragmatic approach — no deterministic signal for non-events.
        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.False(packetReceived);
    }

    // ---------------------------------------------------------------
    // ReceiveError
    // ---------------------------------------------------------------

    [Fact]
    public async Task ReceiveError_CorruptFrame_FiresErrorEvent()
    {
        // KISS frame with invalid escape sequence
        await using MemoryStream stream = new([0xC0, 0x00, 0xDB, 0x42, 0xC0]);
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        TaskCompletionSource<Exception> errorTcs = new();
        aprsModem.ReceiveError += (_, ex) => errorTcs.TrySetResult(ex);

        aprsModem.Start();

        Exception error = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task ReceiveError_KissModemError_FiresErrorEvent()
    {
        await using FailingStream stream = new();
        await using KissModem kissModem = new(stream);
        await using AprsRfModem aprsModem = new(kissModem);

        TaskCompletionSource<Exception> errorTcs = new();
        aprsModem.ReceiveError += (_, ex) => errorTcs.TrySetResult(ex);

        aprsModem.Start();

        Exception error = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<IOException>(error);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static byte[] BuildAx25Frame(string destination, string source, string infoField)
    {
        List<byte> frame =
        [
            // Destination address
            .. EncodeAddress(destination),
            // Source address (last in list)
            .. EncodeAddress(source),
            // Control: UI-frame
            0x03,
            // PID: no layer 3
            0xF0,
            // Info field
            .. Encoding.ASCII.GetBytes(infoField),
        ];

        return [.. frame];
    }

    private static byte[] EncodeAddress(string callsign)
    {
        byte[] bytes = new byte[7];
        string @base = callsign.Contains('-') ? callsign.Split('-')[0] : callsign;
        @base = @base.PadRight(6, ' ');

        for (int i = 0; i < 6; i++)
        {
            bytes[i] = (byte)(@base[i] << 1);
        }

        // SSID byte: bit 0 = 1 (last address), bit 6 = 1 (standard)
        bytes[6] = 0x61;
        return bytes;
    }

    private static byte[] BuildKissFrame(byte[] ax25Data) => BuildKissFrame(0x00, ax25Data);

    private static byte[] BuildKissFrame(byte command, byte[] data) => KissSerializer.EncodeFrame(new KissFrame(command, data));

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Simulated read failure.");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }
}

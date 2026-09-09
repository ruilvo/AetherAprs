// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AetherAprs.Models.Aprs;
using AetherAprs.Modems.Aprs;
using AetherAprs.Modems.Kiss;
using Xunit;

namespace AetherAprs.Tests.Aprs;

public class AprsModemTests
{
    private static readonly Callsign Source = new("N0CALL");
    private static readonly Callsign Dest = new("APZ001");

    // ---------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------

    [Fact]
    public void Constructor_NullKissModem_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new AprsModem(null!));
    }

    // ---------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------

    [Fact]
    public async Task Start_FirstCall_Succeeds()
    {
        await using var stream = new MemoryStream();
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        aprsModem.Start();
        // No exception is the assertion
    }

    [Fact]
    public async Task Start_DoubleCall_ThrowsInvalidOperationException()
    {
        await using var stream = new MemoryStream();
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        aprsModem.Start();
        Assert.Throws<InvalidOperationException>(() => aprsModem.Start());
    }

    [Fact]
    public async Task StopAsync_WithoutStart_IsNoOp()
    {
        await using var stream = new MemoryStream();
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        await aprsModem.StopAsync();
        // No exception is the assertion
    }

    [Fact]
    public async Task StartThenStopAsync_CompletesCleanly()
    {
        await using var stream = new MemoryStream();
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        aprsModem.Start();
        await aprsModem.StopAsync();
    }

    // ---------------------------------------------------------------
    // SendAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task SendAsync_PositionPacket_WritesKissFrame()
    {
        await using var stream = new MemoryStream();
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        var packet = new PositionPacket
        {
            Source = Source,
            Destination = Dest,
            Latitude = 38.5,
            Longitude = -9.10,
            Symbol = new Symbol('/', '#'),
            Precision = 2
        };

        await aprsModem.SendAsync(packet, Source, Dest);

        var written = stream.ToArray();

        // Should start with FEND and end with FEND
        Assert.Equal(0xC0, written[0]);
        Assert.Equal(0xC0, written[^1]);

        // Command byte should be 0x00 (DataFrame, port 0)
        Assert.Equal(0x00, written[1]);
    }

    [Fact]
    public async Task SendAsync_MessagePacket_WritesKissFrame()
    {
        await using var stream = new MemoryStream();
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        var packet = new MessagePacket
        {
            Source = Source,
            Destination = Dest,
            Addressee = new Callsign("OTHER"),
            Text = "Hello"
        };

        await aprsModem.SendAsync(packet, Source, Dest);

        var written = stream.ToArray();
        Assert.Equal(0xC0, written[0]);
        Assert.Equal(0x00, written[1]);
        Assert.Equal(0xC0, written[^1]);
    }

    [Fact]
    public async Task SendAsync_StatusPacket_WritesKissFrame()
    {
        await using var stream = new MemoryStream();
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        var packet = new StatusPacket
        {
            Source = Source,
            Destination = Dest,
            Text = "Online"
        };

        await aprsModem.SendAsync(packet, Source, Dest);

        var written = stream.ToArray();
        Assert.Equal(0xC0, written[0]);
        Assert.Equal(0x00, written[1]);
    }

    [Fact]
    public async Task SendAsync_NullPacket_ThrowsArgumentNullException()
    {
        await using var stream = new MemoryStream();
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            aprsModem.SendAsync(null!, Source, Dest));
    }

    // ---------------------------------------------------------------
    // PacketReceived
    // ---------------------------------------------------------------

    [Fact]
    public async Task PacketReceived_PositionFrame_FiresEvent()
    {
        // Build AX.25 position frame wrapped in KISS
        var ax25Data = BuildAx25Frame("APZ001", "N0CALL", "!3830.00N/00906.00E#");
        var kissBuffer = BuildKissFrame(ax25Data);

        await using var stream = new MemoryStream(kissBuffer);
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        var tcs = new TaskCompletionSource<IAprsPacket>();
        aprsModem.PacketReceived += (_, packet) => tcs.TrySetResult(packet);

        aprsModem.Start();

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(38.5, pos.Latitude, 4);
        Assert.Equal(9.10, pos.Longitude, 4);
        Assert.Equal(new Callsign("N0CALL"), pos.Source);
    }

    [Fact]
    public async Task PacketReceived_MessageFrame_FiresEvent()
    {
        var ax25Data = BuildAx25Frame("APZ001", "N0CALL", ":OTHER   :Hello{5}");
        var kissBuffer = BuildKissFrame(ax25Data);

        await using var stream = new MemoryStream(kissBuffer);
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        var tcs = new TaskCompletionSource<IAprsPacket>();
        aprsModem.PacketReceived += (_, packet) => tcs.TrySetResult(packet);

        aprsModem.Start();

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var msg = Assert.IsType<MessagePacket>(result);
        Assert.Equal("Hello", msg.Text);
        Assert.Equal(5, msg.MessageNumber);
    }

    [Fact]
    public async Task PacketReceived_StatusFrame_FiresEvent()
    {
        var ax25Data = BuildAx25Frame("APZ001", "N0CALL", ">Online via APRS");
        var kissBuffer = BuildKissFrame(ax25Data);

        await using var stream = new MemoryStream(kissBuffer);
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        var tcs = new TaskCompletionSource<IAprsPacket>();
        aprsModem.PacketReceived += (_, packet) => tcs.TrySetResult(packet);

        aprsModem.Start();

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var status = Assert.IsType<StatusPacket>(result);
        Assert.Equal("Online via APRS", status.Text);
    }

    [Fact]
    public async Task PacketReceived_WeatherFrame_FiresEvent()
    {
        var ax25Data = BuildAx25Frame("APZ001", "N0CALL", "_c100s020t080h55b10100");
        var kissBuffer = BuildKissFrame(ax25Data);

        await using var stream = new MemoryStream(kissBuffer);
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        var tcs = new TaskCompletionSource<IAprsPacket>();
        aprsModem.PacketReceived += (_, packet) => tcs.TrySetResult(packet);

        aprsModem.Start();

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var w = Assert.IsType<WeatherPacket>(result);
        Assert.Equal(100, w.WindDirection);
        Assert.Equal(20, w.WindSpeed);
    }

    [Fact]
    public async Task PacketReceived_UnknownPacket_FiresEvent()
    {
        var ax25Data = BuildAx25Frame("APZ001", "N0CALL", "$weird format data");
        var kissBuffer = BuildKissFrame(ax25Data);

        await using var stream = new MemoryStream(kissBuffer);
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        var tcs = new TaskCompletionSource<IAprsPacket>();
        aprsModem.PacketReceived += (_, packet) => tcs.TrySetResult(packet);

        aprsModem.Start();

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsType<UnknownPacket>(result);
    }

    [Fact]
    public async Task PacketReceived_NonDataFrame_DoesNotFireEvent()
    {
        // KISS TXDelay command frame (not a data frame)
        var kissBuffer = BuildKissFrame(0x01, [0x05]);

        await using var stream = new MemoryStream(kissBuffer);
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        var packetReceived = false;
        aprsModem.PacketReceived += (_, _) => packetReceived = true;

        aprsModem.Start();

        // Wait a bit — no event should fire
        await Task.Delay(500);

        Assert.False(packetReceived);
    }

    // ---------------------------------------------------------------
    // ReceiveError
    // ---------------------------------------------------------------

    [Fact]
    public async Task ReceiveError_CorruptFrame_FiresErrorEvent()
    {
        // KISS frame with invalid escape sequence
        await using var stream = new MemoryStream([0xC0, 0x00, 0xDB, 0x42, 0xC0]);
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        var errorTcs = new TaskCompletionSource<Exception>();
        aprsModem.ReceiveError += (_, ex) => errorTcs.TrySetResult(ex);

        aprsModem.Start();

        var error = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(error);
    }

    [Fact]
    public async Task ReceiveError_KissModemError_FiresErrorEvent()
    {
        await using var stream = new FailingStream();
        await using var kissModem = new KissModem(stream);
        await using var aprsModem = new AprsModem(kissModem);

        var errorTcs = new TaskCompletionSource<Exception>();
        aprsModem.ReceiveError += (_, ex) => errorTcs.TrySetResult(ex);

        aprsModem.Start();

        var error = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsType<IOException>(error);
    }

    // ---------------------------------------------------------------
    // IAprsPacket dispatch pattern
    // ---------------------------------------------------------------

    [Fact]
    public void PacketReceived_SwitchOnType_Works()
    {
        // Verify that the consumer can switch on IAprsPacket
        IAprsPacket[] packets =
        [
            new PositionPacket
            {
                Source = Source, Destination = Dest, Latitude = 38.5, Longitude = -9.10,
                Symbol = new Symbol('/', '#')
            },
            new MessagePacket
            {
                Source = Source, Destination = Dest, Addressee = new Callsign("OTHER"), Text = "Hi"
            },
            new StatusPacket
            {
                Source = Source, Destination = Dest, Text = "Online"
            },
            new WeatherPacket
            {
                Source = Source, Destination = Dest, WindDirection = 90, WindSpeed = 15
            },
            new UnknownPacket
            {
                Source = Source, Destination = Dest, Raw = "???"
            }
        ];

        int position = 0, message = 0, status = 0, weather = 0, unknown = 0;

        foreach (var packet in packets)
        {
            switch (packet)
            {
                case PositionPacket: position++; break;
                case MessagePacket: message++; break;
                case StatusPacket: status++; break;
                case WeatherPacket: weather++; break;
                case UnknownPacket: unknown++; break;
            }
        }

        Assert.Equal(1, position);
        Assert.Equal(1, message);
        Assert.Equal(1, status);
        Assert.Equal(1, weather);
        Assert.Equal(1, unknown);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static byte[] BuildAx25Frame(string destination, string source, string infoField)
    {
        var frame = new System.Collections.Generic.List<byte>();

        // Destination address
        frame.AddRange(EncodeAddress(destination));
        // Source address (last in list)
        frame.AddRange(EncodeAddress(source));

        // Control: UI-frame
        frame.Add(0x03);
        // PID: no layer 3
        frame.Add(0xF0);
        // Info field
        frame.AddRange(Encoding.ASCII.GetBytes(infoField));

        return [.. frame];
    }

    private static byte[] EncodeAddress(string callsign)
    {
        var bytes = new byte[7];
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

    private static byte[] BuildKissFrame(byte[] ax25Data)
    {
        return BuildKissFrame(0x00, ax25Data);
    }

    private static byte[] BuildKissFrame(byte command, byte[] data)
    {
        return KissSerializer.EncodeFrame(new KissFrame(command, data));
    }

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

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new IOException("Simulated read failure.");
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
    }
}
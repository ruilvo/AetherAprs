// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Data;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Kiss;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using AetherAprs.Transports.Kiss;
using Geo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

/// <summary>
/// End-to-end tests for the receive pipeline:
/// modem PacketReceived → PortService queue → storage → PacketReceived event → digipeater.
/// </summary>
public sealed class PortServiceReceivePipelineTests
{
    [Fact]
    public async Task ReceivedFrame_StoresPacketAndRaisesPacketReceived()
    {
        var portId = Guid.NewGuid();
        var kissBuffer = BuildKissFrame(BuildAx25Frame("APZ001", "N0CALL", "!3830.00N/00906.00E#"));
        using var db = TempAppDatabase.Create();
        var storage = new RecordingPacketStorageService();
        var digipeater = new RecordingDigipeaterService();
        var kissSettings = new KissSettings
        {
            Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
        };
        await using var service = CreateService(db, storage, digipeater, kissBuffer, scriptedPortSettings: kissSettings);

        var port = new PortConfig
        {
            Id = portId,
            Name = "RF",
            IsEnabled = false,
            IsRx = true,
            IsTx = false,
            TypeSettings = kissSettings
        };
        await service.AddPortAsync(port);

        var receivedTcs = new TaskCompletionSource<PortPacketReceivedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.PacketReceived += (_, args) => receivedTcs.TrySetResult(args);

        await service.SetPortEnabledAsync(portId, true);

        var args = await receivedTcs.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Equal(portId, args.PortId);
        var position = Assert.IsType<PositionPacket>(args.Packet);
        Assert.Equal(new Callsign("N0CALL"), position.Source);

        var stored = Assert.Single(storage.StoredPackets);
        Assert.Equal(portId, stored.PortId);
        Assert.False(stored.IsOutbound);
        Assert.Same(args.Packet, stored.Packet);
        Assert.Null(storage.LastException);

        var digipeatCall = Assert.Single(digipeater.Calls);
        Assert.Equal(portId, digipeatCall.SourcePortId);
        Assert.Same(args.Packet, digipeatCall.Packet);
        Assert.True(digipeatCall.SourcePortIsAprsIs is false);

        await service.StopAllPortsAsync();
    }

    [Fact]
    public async Task ReceivedFrame_StorageThrows_StillRaisesPacketReceived()
    {
        var portId = Guid.NewGuid();
        var kissBuffer = BuildKissFrame(BuildAx25Frame("APZ001", "N0CALL", "!3830.00N/00906.00E#"));
        using var db = TempAppDatabase.Create();
        var storage = new RecordingPacketStorageService
        {
            ThrownException = new InvalidOperationException("storage down")
        };
        var digipeater = new RecordingDigipeaterService();
        var kissSettings = new KissSettings
        {
            Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
        };
        await using var service = CreateService(db, storage, digipeater, kissBuffer, scriptedPortSettings: kissSettings);

        var port = new PortConfig
        {
            Id = portId,
            Name = "RF",
            IsEnabled = false,
            IsRx = true,
            IsTx = false,
            TypeSettings = kissSettings
        };
        await service.AddPortAsync(port);

        var receivedTcs = new TaskCompletionSource<PortPacketReceivedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.PacketReceived += (_, args) => receivedTcs.TrySetResult(args);

        await service.SetPortEnabledAsync(portId, true);

        var args = await receivedTcs.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Equal(portId, args.PortId);
        Assert.Equal(1, storage.StoreAttempts);
        Assert.NotNull(storage.LastException);

        await service.StopAllPortsAsync();
    }

    [Fact]
    public async Task ReceivedFrame_DigipeaterTargetsOtherPort_PacketSentToTarget()
    {
        var rfPortId = Guid.NewGuid();
        var targetPortId = Guid.NewGuid();
        var kissBuffer = BuildKissFrame(BuildAx25Frame("APZ001", "N0CALL", "!3830.00N/00906.00E#"));
        using var db = TempAppDatabase.Create();
        var storage = new RecordingPacketStorageService();

        // The target port gets a writable stream we can inspect for the
        // digipeated (transmitted) frame.
        var targetWriteTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var digipeater = new RecordingDigipeaterService
        {
            OnGetDigipeatTargets = (packet, _, _, _) =>
            {
                return
                [
                    new DigipeatTarget(targetPortId, packet)
                ];
            }
        };

        var rfKissSettings = new KissSettings
        {
            Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
        };

        await using var service = CreateService(
            db, storage, digipeater, kissBuffer, targetWriteTcs, scriptedPortSettings: rfKissSettings);

        var rfPort = new PortConfig
        {
            Id = rfPortId,
            Name = "RF",
            IsEnabled = false,
            IsRx = true,
            IsTx = false,
            TypeSettings = rfKissSettings
        };
        var targetPort = new PortConfig
        {
            Id = targetPortId,
            Name = "Target",
            IsEnabled = false,
            IsRx = true,
            IsTx = true,
            TypeSettings = new KissSettings
            {
                Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8002 }
            }
        };

        await service.AddPortAsync(rfPort);
        await service.AddPortAsync(targetPort);

        // Enable the target first so it has an active session by the time the
        // receive pipeline digipeats the frame from the RF port.
        await service.SetPortEnabledAsync(targetPortId, true);
        await service.SetPortEnabledAsync(rfPortId, true);

        // Wait until digipeater has decided on targets and the send attempt completed.
        var sent = await targetWriteTcs.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        // The digipeated frame must carry the packet's AX.25 payload inside a KISS frame.
        Assert.Contains<byte>((byte)0xC0, sent);
        Assert.NotEmpty(sent);

        await service.StopAllPortsAsync();
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static PortService CreateService(
        TempAppDatabase db,
        RecordingPacketStorageService storage,
        RecordingDigipeaterService digipeater,
        byte[]? injectedKissBuffer = null,
        TaskCompletionSource<byte[]>? targetWriteTcs = null,
        KissSettings? scriptedPortSettings = null)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        var kissStreamFactory = new ScriptedKissStreamFactory(scriptedPortSettings, injectedKissBuffer, targetWriteTcs);
        return new PortService(
            db.Factory,
            new TestConfigurationService(),
            services.GetRequiredService<ILoggerFactory>(),
            NullLogger<PortService>.Instance,
            kissStreamFactory,
            storage,
            Substitute.For<IForegroundService>(),
            digipeater);
    }

    private static byte[] BuildAx25Frame(string destination, string source, string infoField)
    {
        var frame = new List<byte>();
        frame.AddRange(EncodeAddress(destination));
        frame.AddRange(EncodeAddress(source));
        frame.Add(0x03); // Control: UI-frame
        frame.Add(0xF0); // PID: no layer 3
        frame.AddRange(Encoding.ASCII.GetBytes(infoField));
        return [.. frame];
    }

    private static byte[] EncodeAddress(string callsign)
    {
        var bytes = new byte[7];
        var @base = callsign.Split('-')[0].PadRight(6, ' ');

        for (var i = 0; i < 6; i++)
        {
            bytes[i] = (byte)(@base[i] << 1);
        }

        // SSID byte: bit 0 = 1 (last address), bit 6 = 1 (standard)
        bytes[6] = 0x61;
        return bytes;
    }

    private static byte[] BuildKissFrame(byte[] ax25Data)
    {
        return KissSerializer.EncodeFrame(new KissFrame(0x00, ax25Data));
    }

    private sealed record StoredPacketCall(AprsPacket Packet, Guid? PortId, bool IsOutbound);

    private sealed record DigipeatCall(
        AprsPacket Packet,
        Guid SourcePortId,
        bool SourcePortIsAprsIs,
        IReadOnlyList<PortInfo> AvailablePorts);

    private sealed class RecordingPacketStorageService : IPacketStorageService
    {
#pragma warning disable CS0067 // Event is never used - required by interface
        public event EventHandler? PacketStored;
#pragma warning restore CS0067

        public List<StoredPacketCall> StoredPackets { get; } = [];

        public int StoreAttempts { get; private set; }

        public Exception? LastException { get; private set; }

        public Exception? ThrownException { get; set; }

        public Task StorePacketAsync(
            AprsPacket packet,
            Guid? portId,
            bool isOutbound = false,
            CancellationToken cancellationToken = default)
        {
            StoreAttempts++;
            if (ThrownException is not null)
            {
                LastException = ThrownException;
                throw ThrownException;
            }

            StoredPackets.Add(new StoredPacketCall(packet, portId, isOutbound));
            return Task.CompletedTask;
        }

        public Task CleanupOldPacketsAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDigipeaterService : IDigipeaterService
    {
        public List<DigipeatCall> Calls { get; } = [];

        public Func<AprsPacket, Guid, bool, IReadOnlyList<PortInfo>, IReadOnlyList<DigipeatTarget>>?
            OnGetDigipeatTargets { get; set; }

        public IReadOnlyList<DigipeatTarget> GetDigipeatTargets(
            AprsPacket packet,
            Guid sourcePortId,
            bool sourcePortIsAprsIs,
            IReadOnlyList<PortInfo> availablePorts)
        {
            Calls.Add(new DigipeatCall(packet, sourcePortId, sourcePortIsAprsIs, availablePorts));
            return OnGetDigipeatTargets?.Invoke(packet, sourcePortId, sourcePortIsAprsIs, availablePorts)
                ?? [];
        }
    }

    /// <summary>
    /// Returns a pre-buffered (never-EOF) stream for the port whose KissSettings
    /// instance matches the source key, and a writable in-memory stream for other
    /// ports whose writes signal a TCS.
    /// </summary>
    private sealed class ScriptedKissStreamFactory(
        KissSettings? scriptedPortSettings,
        byte[]? firstPortBuffer,
        TaskCompletionSource<byte[]>? targetWriteTcs) : IKissStreamFactory
    {
        public IReadOnlyCollection<Type> SupportedTransports { get; } =
            [typeof(TcpKissTransportSettings)];

        public Task<Stream> OpenAsync(KissSettings settings, CancellationToken cancellationToken = default)
        {
            if (scriptedPortSettings is not null &&
                firstPortBuffer is not null &&
                ReferenceEquals(settings, scriptedPortSettings))
            {
                return Task.FromResult<Stream>(new ScriptedReadStream(firstPortBuffer));
            }

            return Task.FromResult<Stream>(new RecordingWriteStream(targetWriteTcs));
        }
    }

    /// <summary>A read-only stream serving a fixed buffer that blocks at EOF.</summary>
    private sealed class ScriptedReadStream(byte[] buffer) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => buffer.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadCore(buffer.AsSpan(offset, count));
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = ReadCore(buffer.Span);
            while (read == 0 && !cancellationToken.IsCancellationRequested)
            {
                // Hold the read loop open (never-EOF) until cancellation.
                try
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return 0;
                }

                read = ReadCore(buffer.Span);
            }

            return read;
        }

        private int ReadCore(Span<byte> destination)
        {
            var remaining = buffer.Length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            var count = Math.Min(remaining, destination.Length);
            buffer.AsSpan(_position, count).CopyTo(destination);
            _position += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            // The KISS modem requires a writable stream (TX side unused here).
        }
    }

    /// <summary>A write-capturing stream whose reads block until cancellation.</summary>
    private sealed class RecordingWriteStream(TaskCompletionSource<byte[]>? writeTcs) : Stream
    {
        private readonly MemoryStream _written = new();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written.Length;
        public override long Position { get => _written.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _written.Write(buffer, offset, count);
            writeTcs?.TrySetResult(_written.ToArray());
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return 0;
            }

            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
        public AppSettings Settings { get; } = new();

        public Task SaveSettingsAsync() => Task.CompletedTask;

        public Task UpdateSettingsAsync(Action<AppSettings> updateAction)
        {
            updateAction(Settings);
            return Task.CompletedTask;
        }

        public AppSettings GetSettingsSnapshot() => Settings;
    }
}

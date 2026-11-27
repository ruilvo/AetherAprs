// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Kiss;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using AetherAprs.Transports.Kiss;
using Geo;
using Microsoft.EntityFrameworkCore;
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
        Guid portId = Guid.NewGuid();
        byte[] kissBuffer = BuildKissFrame(BuildAx25Frame("APZ001", "N0CALL", "!3830.00N/00906.00E#"));
        using TempAppDatabase db = TempAppDatabase.Create();
        RecordingPacketStorageService storage = new();
        RecordingDigipeaterService digipeater = new();
        KissSettings kissSettings = new()
        {
            Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
        };
        await using PortService service = CreateService(db, storage, digipeater, kissBuffer, scriptedPortSettings: kissSettings);

        PortConfig port = new()
        {
            Id = portId,
            Name = "RF",
            IsEnabled = false,
            IsRx = true,
            IsTx = false,
            TypeSettings = kissSettings
        };
        await service.AddPortAsync(port);

        TaskCompletionSource<PortPacketReceivedEventArgs> receivedTcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.PacketReceived += (_, args) => receivedTcs.TrySetResult(args);

        await service.SetPortEnabledAsync(portId, true);

        PortPacketReceivedEventArgs args = await receivedTcs.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Equal(portId, args.PortId);
        PositionPacket position = Assert.IsType<PositionPacket>(args.Packet);
        Assert.Equal(new Callsign("N0CALL"), position.Source);

        StoredPacketCall stored = Assert.Single(storage.StoredPackets);
        Assert.Equal(portId, stored.PortId);
        Assert.False(stored.IsOutbound);
        Assert.Same(args.Packet, stored.Packet);
        Assert.Null(storage.LastException);

        await digipeater.FirstCall.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        DigipeatCall digipeatCall = Assert.Single(digipeater.Calls);
        Assert.Equal(portId, digipeatCall.SourcePortId);
        Assert.Same(args.Packet, digipeatCall.Packet);
        Assert.True(digipeatCall.SourcePortIsAprsIs is false);

        await service.StopAllPortsAsync();
    }

    [Fact]
    public async Task ReceivedFrame_StorageThrows_DoesNotPublishAndCountsDrop()
    {
        Guid portId = Guid.NewGuid();
        byte[] kissBuffer = BuildKissFrame(BuildAx25Frame("APZ001", "N0CALL", "!3830.00N/00906.00E#"));
        using TempAppDatabase db = TempAppDatabase.Create();
        RecordingPacketStorageService storage = new()
        {
            ThrownException = new InvalidOperationException("storage down")
        };
        RecordingDigipeaterService digipeater = new();
        RecordingDiagnosticSink diagnostics = new();
        KissSettings kissSettings = new()
        {
            Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
        };
        await using PortService service = CreateService(
            db,
            storage,
            digipeater,
            kissBuffer,
            scriptedPortSettings: kissSettings,
            diagnosticSink: diagnostics);

        PortConfig port = new()
        {
            Id = portId,
            Name = "RF",
            IsEnabled = false,
            IsRx = true,
            IsTx = false,
            TypeSettings = kissSettings
        };
        await service.AddPortAsync(port);

        int published = 0;
        service.PacketReceived += (_, _) => Interlocked.Increment(ref published);

        await service.SetPortEnabledAsync(portId, true);

        // The drop diagnostic is recorded after storage fails, so waiting for it
        // proves the packet finished processing without being published.
        await diagnostics.Recorded.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, published);
        Assert.Equal(1, storage.StoreAttempts);
        Assert.NotNull(storage.LastException);
        Assert.Equal(1, service.GetDroppedPacketCount(portId));
        Assert.Equal(1, service.TotalDroppedPackets);

        DiagnosticEvent dropEvent = Assert.Single(diagnostics.Events);
        Assert.Equal("ingest", dropEvent.Category);
        Assert.Equal("Dropped", dropEvent.State);
        Assert.Equal(DiagnosticSeverity.Error, dropEvent.Severity);

        await service.StopAllPortsAsync();
    }

    [Fact]
    public async Task ReceivedFrames_QueueFull_CountsDropsInsteadOfBlocking()
    {
        Guid portId = Guid.NewGuid();
        byte[] frame = BuildKissFrame(BuildAx25Frame("APZ001", "N0CALL", "!3830.00N/00906.00E#"));
        byte[] kissBuffer = [.. frame, .. frame, .. frame];
        using TempAppDatabase db = TempAppDatabase.Create();
        RecordingPacketStorageService storage = new();
        TaskCompletionSource<bool> storeEntered = storage.BlockFirstStore();
        RecordingDigipeaterService digipeater = new();
        KissSettings kissSettings = new()
        {
            Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
        };
        await using PortService service = CreateService(
            db,
            storage,
            digipeater,
            kissBuffer,
            scriptedPortSettings: kissSettings,
            packetQueueCapacity: 1);

        PortConfig port = new()
        {
            Id = portId,
            Name = "RF",
            IsEnabled = false,
            IsRx = true,
            IsTx = false,
            TypeSettings = kissSettings
        };
        await service.AddPortAsync(port);

        await service.SetPortEnabledAsync(portId, true);

        // Hold the single consumer inside storage so the queue stays full while
        // the remaining frames arrive.
        await storeEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);
        await WaitForIngestDropsAsync(service, portId);

        Assert.True(
            service.GetDroppedPacketCount(portId) >= 1,
            "A full inbound queue must count the dropped packets.");
        Assert.Equal(service.TotalDroppedPackets, service.GetDroppedPacketCount(portId));

        storage.ReleaseBlockedStore();
        await service.StopAllPortsAsync();
    }

    private static async Task WaitForIngestDropsAsync(PortService service, Guid portId)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (service.GetDroppedPacketCount(portId) == 0)
        {
            if (DateTime.UtcNow >= deadline)
            {
                return;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static async Task WaitForForwardDropsAsync(PortService service, Guid portId)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (service.GetDroppedForwardCount(portId) == 0)
        {
            if (DateTime.UtcNow >= deadline)
            {
                return;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ReceivedFrame_DigipeaterTargetsOtherPort_PacketSentToTarget()
    {
        Guid rfPortId = Guid.NewGuid();
        Guid targetPortId = Guid.NewGuid();
        byte[] kissBuffer = BuildKissFrame(BuildAx25Frame("APZ001", "N0CALL", "!3830.00N/00906.00E#"));
        using TempAppDatabase db = TempAppDatabase.Create();
        RecordingPacketStorageService storage = new();

        // The target port gets a writable stream we can inspect for the
        // digipeated (transmitted) frame.
        TaskCompletionSource<byte[]> targetWriteTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingDigipeaterService digipeater = new()
        {
            OnGetDigipeatTargets = (packet, _, _, _) => [
                    new DigipeatTarget(targetPortId, packet)
                ]
        };

        KissSettings rfKissSettings = new()
        {
            Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
        };

        await using PortService service = CreateService(
            db, storage, digipeater, kissBuffer, targetWriteTcs, scriptedPortSettings: rfKissSettings);

        PortConfig rfPort = new()
        {
            Id = rfPortId,
            Name = "RF",
            IsEnabled = false,
            IsRx = true,
            IsTx = false,
            TypeSettings = rfKissSettings
        };
        PortConfig targetPort = new()
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
        byte[] sent = await targetWriteTcs.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        // The digipeated frame must carry the packet's AX.25 payload inside a KISS frame.
        Assert.Contains<byte>((byte)0xC0, sent);
        Assert.NotEmpty(sent);

        await service.StopAllPortsAsync();
    }

    [Fact]
    public async Task ReceivedFrames_DigipeatPending_DoesNotBlockIngest()
    {
        const int ordinaryPacketCount = 20;
        Guid portId = Guid.NewGuid();
        byte[] firstFrame = BuildKissFrame(BuildAx25Frame("APZ001", "N0CALL", "!3830.00N/00906.00E#"));
        byte[] ordinaryFrame = BuildKissFrame(BuildAx25Frame("APZ002", "N0CALL", "!3830.00N/00906.00E#"));
        byte[] kissBuffer = [.. firstFrame
, .. Enumerable.Repeat(ordinaryFrame, ordinaryPacketCount).SelectMany(frame => frame)];
        using TempAppDatabase db = TempAppDatabase.Create();
        RecordingPacketStorageService storage = new();
        using BlockingDigipeaterService digipeater = new(expectedCalls: ordinaryPacketCount + 1);
        KissSettings kissSettings = new()
        {
            Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
        };
        await using PortService service = CreateService(
            db,
            storage,
            digipeater,
            kissBuffer,
            scriptedPortSettings: kissSettings);

        PortConfig port = new()
        {
            Id = portId,
            Name = "RF",
            IsEnabled = false,
            IsRx = true,
            IsTx = false,
            TypeSettings = kissSettings
        };
        await service.AddPortAsync(port);

        int published = 0;
        TaskCompletionSource<bool> allPublished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.PacketReceived += (_, _) =>
        {
            if (Interlocked.Increment(ref published) == ordinaryPacketCount + 1)
            {
                allPublished.TrySetResult(true);
            }
        };

        try
        {
            await service.SetPortEnabledAsync(portId, true);

            await digipeater.Entered.Task.WaitAsync(
                TimeSpan.FromSeconds(3),
                TestContext.Current.CancellationToken);
            await allPublished.Task.WaitAsync(
                TimeSpan.FromSeconds(3),
                TestContext.Current.CancellationToken);

            // The first candidate is held inside the digipeater, yet every later
            // packet was stored and published: ingest is decoupled from relay.
            Assert.False(
                digipeater.Drained.Task.IsCompleted,
                "Digipeat should still be pending while ingest drains.");
            Assert.Equal(ordinaryPacketCount + 1, Volatile.Read(ref published));
            Assert.Equal(ordinaryPacketCount + 1, storage.StoredPackets.Count);
            Assert.Equal(0, service.TotalDroppedPackets);
        }
        finally
        {
            digipeater.Release();
        }

        await digipeater.Drained.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);
        Assert.Equal(ordinaryPacketCount + 1, digipeater.Calls.Count);
    }

    [Fact]
    public async Task ReceivedFrames_DigipeatQueueFull_CountsDrops()
    {
        const int frameCount = 40;
        Guid portId = Guid.NewGuid();
        byte[] frame = BuildKissFrame(BuildAx25Frame("APZ001", "N0CALL", "!3830.00N/00906.00E#"));
        byte[] kissBuffer = [.. Enumerable.Repeat(frame, frameCount).SelectMany(bytes => bytes)];
        using TempAppDatabase db = TempAppDatabase.Create();
        RecordingPacketStorageService storage = new();
        BlockingDigipeaterService digipeater = new(expectedCalls: 1);
        RecordingDiagnosticSink diagnostics = new();
        KissSettings kissSettings = new()
        {
            Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
        };
        await using PortService service = CreateService(
            db,
            storage,
            digipeater,
            kissBuffer,
            scriptedPortSettings: kissSettings,
            diagnosticSink: diagnostics,
            digipeatQueueCapacity: 2);

        PortConfig port = new()
        {
            Id = portId,
            Name = "RF",
            IsEnabled = false,
            IsRx = true,
            IsTx = false,
            TypeSettings = kissSettings
        };
        await service.AddPortAsync(port);

        try
        {
            await service.SetPortEnabledAsync(portId, true);

            // Hold the digipeat consumer on the first candidate so the tiny queue
            // fills while the remaining candidates arrive.
            await digipeater.Entered.Task.WaitAsync(
                TimeSpan.FromSeconds(3),
                TestContext.Current.CancellationToken);
            await WaitForForwardDropsAsync(service, portId);
            Assert.True(
                await WaitForDropReasonAsync(diagnostics, "Digipeat queue full; candidate dropped."),
                "A full digipeat queue must record the digipeat drop reason.");

            Assert.True(
                service.GetDroppedForwardCount(portId) >= 1,
                "A full digipeat queue must count the discarded candidates.");
            Assert.Equal(0, service.TotalDroppedPackets);
            Assert.DoesNotContain(
                diagnostics.Events,
                diagnosticEvent => diagnosticEvent.Reason == "Inbound queue full; newest packet dropped.");
        }
        finally
        {
            digipeater.Release();
        }
    }

    private static async Task<bool> WaitForDropReasonAsync(RecordingDiagnosticSink diagnostics, string reason)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!diagnostics.Events.Any(
                   diagnosticEvent => diagnosticEvent.State == "Dropped" && diagnosticEvent.Reason == reason))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        return true;
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static PortService CreateService(
        TempAppDatabase db,
        RecordingPacketStorageService storage,
        IDigipeaterService digipeater,
        byte[]? injectedKissBuffer = null,
        TaskCompletionSource<byte[]>? targetWriteTcs = null,
        KissSettings? scriptedPortSettings = null,
        IDiagnosticEventSink? diagnosticSink = null,
        int packetQueueCapacity = 1024,
        int digipeatQueueCapacity = 256)
    {
        ScriptedKissStreamFactory kissStreamFactory = new(scriptedPortSettings, injectedKissBuffer, targetWriteTcs);
        return TestPortService.Create(
            db,
            TestSessionManager.Create(TestModemFactory.Create(kissStreamFactory: kissStreamFactory)),
            new TestPortService.PortServiceCollaborators
            {
                PacketStorageService = storage,
                DigipeaterService = digipeater,
                KeepAliveService = Substitute.For<IKeepAliveService>(),
                DiagnosticSink = diagnosticSink,
                IngestOptions = new PacketIngestOptions { QueueCapacity = packetQueueCapacity },
                DigipeatOptions = new DigipeatRouterOptions { QueueCapacity = digipeatQueueCapacity }
            });
    }

    private static byte[] BuildAx25Frame(string destination, string source, string infoField)
    {
        List<byte> frame =
        [
            .. EncodeAddress(destination),
            .. EncodeAddress(source),
            0x03, // Control: UI-frame
            0xF0, // PID: no layer 3
            .. Encoding.ASCII.GetBytes(infoField),
        ];
        return [.. frame];
    }

    private static byte[] EncodeAddress(string callsign)
    {
        byte[] bytes = new byte[7];
        string @base = callsign.Split('-')[0].PadRight(6, ' ');

        for (int i = 0; i < 6; i++)
        {
            bytes[i] = (byte)(@base[i] << 1);
        }

        // SSID byte: bit 0 = 1 (last address), bit 6 = 1 (standard)
        bytes[6] = 0x61;
        return bytes;
    }

    private static byte[] BuildKissFrame(byte[] ax25Data) => KissSerializer.EncodeFrame(new KissFrame(0x00, ax25Data));

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
        public event EventHandler<StoragePressureEventArgs>? StoragePressureDetected;
#pragma warning restore CS0067

        private TaskCompletionSource<bool>? _storeGate;
        private TaskCompletionSource<bool>? _storeEntered;

        public List<StoredPacketCall> StoredPackets { get; } = [];

        public int StoreAttempts { get; private set; }

        public Exception? LastException { get; private set; }

        public Exception? ThrownException { get; set; }

        /// <summary>Blocks the first store until <see cref="ReleaseBlockedStore"/> is called.</summary>
        public TaskCompletionSource<bool> BlockFirstStore()
        {
            _storeEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _storeGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _storeEntered;
        }

        public void ReleaseBlockedStore() => _storeGate?.TrySetResult(true);

        public async Task StorePacketAsync(
            AprsPacket packet,
            Guid? portId,
            bool isOutbound = false,
            CancellationToken cancellationToken = default)
        {
            StoreAttempts++;

            if (_storeGate is not null)
            {
                _storeEntered?.TrySetResult(true);
                await _storeGate.Task.WaitAsync(cancellationToken);
                _storeGate = null;
            }

            if (ThrownException is not null)
            {
                LastException = ThrownException;
                throw ThrownException;
            }

            StoredPackets.Add(new StoredPacketCall(packet, portId, isOutbound));
        }

        public Task EnsureOutboundMessageAsync(MessagePacket packet, Guid? portId, int retryCount,
            DateTimeOffset? nextRetryTime, Guid? logicalId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateOutboundMessageStatusAsync(AprsAddress addressee, int messageNumber,
            AetherAprs.Models.Messaging.MessageDeliveryStatus deliveryStatus, int retryCount,
            DateTimeOffset? nextRetryTime, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CleanupOldPacketsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingDiagnosticSink : IDiagnosticEventSink
    {
        private readonly BoundedDiagnosticEventStore _inner = new();

        public TaskCompletionSource<bool> Recorded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<DiagnosticEvent> Events => _inner.Events;

        public void Record(
            string category,
            DiagnosticSeverity severity,
            string state,
            string reason,
            string? safeIdentifier = null)
        {
            _inner.Record(category, severity, state, reason, safeIdentifier);
            Recorded.TrySetResult(true);
        }

        public void Clear() => _inner.Clear();
    }

    private sealed class RecordingDigipeaterService : IDigipeaterService
    {
        public List<DigipeatCall> Calls { get; } = [];

        /// <summary>
        /// Signalled after the first target query so tests can observe the
        /// digipeat decision without racing the packet-processing consumer.
        /// </summary>
        public TaskCompletionSource<bool> FirstCall { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<AprsPacket, Guid, bool, IReadOnlyList<PortInfo>, IReadOnlyList<DigipeatTarget>>?
            OnGetDigipeatTargets
        { get; set; }

        public IReadOnlyList<DigipeatTarget> GetDigipeatTargets(
            AprsPacket packet,
            Guid sourcePortId,
            bool sourcePortIsAprsIs,
            IReadOnlyList<PortInfo> availablePorts)
        {
            Calls.Add(new DigipeatCall(packet, sourcePortId, sourcePortIsAprsIs, availablePorts));
            FirstCall.TrySetResult(true);
            return OnGetDigipeatTargets?.Invoke(packet, sourcePortId, sourcePortIsAprsIs, availablePorts)
                ?? [];
        }

        public IReadOnlyList<DigipeatDecision> Evaluate(
            AprsPacket packet,
            Guid sourcePortId,
            bool sourcePortIsAprsIs,
            IReadOnlyList<PortInfo> availablePorts) => [];

        public IReadOnlyList<DigipeatDecisionRecord> GetRecentDecisions(int maximum = DigipeatDecisionLog.DefaultCapacity) => [];

        public long GetDecisionCount(DigipeatReason reason) => 0;
    }

    /// <summary>
    /// Blocks the digipeat consumer inside the first target query so tests can
    /// prove ingest continues while a relay is pending.
    /// </summary>
    private sealed class BlockingDigipeaterService(int expectedCalls) : IDigipeaterService, IDisposable
    {
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);
        private readonly Lock _gate = new();
        // A kernel event, not a TaskCompletionSource: Release has to wake this thread
        // directly. Completing a task instead schedules the wake as a thread-pool
        // continuation, and a full parallel run can starve the pool long enough for
        // the drain wait below to expire even though the release already happened.
        private readonly ManualResetEventSlim _release = new(false);
        private readonly List<DigipeatCall> _calls = [];

        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Drained { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<DigipeatCall> Calls
        {
            get
            {
                lock (_gate)
                {
                    return [.. _calls];
                }
            }
        }

        public void Release() => _release.Set();

        public void Dispose() => _release.Dispose();

        public IReadOnlyList<DigipeatTarget> GetDigipeatTargets(
            AprsPacket packet,
            Guid sourcePortId,
            bool sourcePortIsAprsIs,
            IReadOnlyList<PortInfo> availablePorts)
        {
            Entered.TrySetResult(true);
            _release.Wait(WaitTimeout);

            lock (_gate)
            {
                _calls.Add(new DigipeatCall(packet, sourcePortId, sourcePortIsAprsIs, availablePorts));
                if (_calls.Count >= expectedCalls)
                {
                    Drained.TrySetResult(true);
                }
            }

            return [];
        }

        public IReadOnlyList<DigipeatDecision> Evaluate(
            AprsPacket packet,
            Guid sourcePortId,
            bool sourcePortIsAprsIs,
            IReadOnlyList<PortInfo> availablePorts) => [];

        public IReadOnlyList<DigipeatDecisionRecord> GetRecentDecisions(int maximum = DigipeatDecisionLog.DefaultCapacity) => [];

        public long GetDecisionCount(DigipeatReason reason) => 0;
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
            // Ports publish copied configuration, so identity cannot be used to
            // tell the scripted receive port from a plain transmit port.
            if (scriptedPortSettings is not null &&
                firstPortBuffer is not null &&
                settings.Transport is TcpKissTransportSettings tcp &&
                scriptedPortSettings.Transport is TcpKissTransportSettings scriptedTcp &&
                tcp.Port == scriptedTcp.Port)
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

        public override int Read(byte[] buffer, int offset, int count) => ReadCore(buffer.AsSpan(offset, count));

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int read = ReadCore(buffer.Span);
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
            int remaining = buffer.Length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            int count = Math.Min(remaining, destination.Length);
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

}

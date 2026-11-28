// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using AetherAprs.Transports.Kiss;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.Services.Ports;

/// <summary>
/// Regression tests: supervision must start and stop modems
/// without rewriting the user's persisted <see cref="PortConfig.IsEnabled"/>.
/// </summary>
public sealed class PortServiceSessionControllerTests
{
    [Fact]
    public async Task ConnectAsync_StartsModemWithoutPersistingEnabledIntent()
    {
        Guid portId = Guid.NewGuid();
        PortConfig port = CreatePort(portId, isEnabled: false);
        using TempAppDatabase db = TempAppDatabase.Create(port);
        TrackingKissStreamFactory factory = new();
        await using PortService service = CreateService(db, factory);
        await service.InitializeAsync(TestContext.Current.CancellationToken);
        PortServiceSessionController controller = new(service);

        await controller.ConnectAsync(portId, TestContext.Current.CancellationToken);

        Assert.Equal(1, factory.OpenCount);
        Assert.False(service.Ports.Single(p => p.Id == portId).IsEnabled);

        await using AppDbContext verification = db.CreateContext();
        PortRecord? record = await verification.Ports.FindAsync([portId], TestContext.Current.CancellationToken);
        Assert.NotNull(record);
        Assert.False(record.IsEnabled);

        await service.StopAllPortsAsync();
    }

    [Fact]
    public async Task DisconnectAsync_StopsModemWithoutClearingPersistedIntent()
    {
        Guid portId = Guid.NewGuid();
        PortConfig port = CreatePort(portId, isEnabled: true);
        using TempAppDatabase db = TempAppDatabase.Create(port);
        TrackingKissStreamFactory factory = new();
        await using PortService service = CreateService(db, factory);
        await service.InitializeAsync(TestContext.Current.CancellationToken);
        PortServiceSessionController controller = new(service);

        await controller.ConnectAsync(portId, TestContext.Current.CancellationToken);
        await controller.DisconnectAsync(portId, TestContext.Current.CancellationToken);

        Assert.Equal(1, factory.OpenCount);
        Assert.Equal(1, factory.StreamDisposeCount);
        Assert.True(service.Ports.Single(p => p.Id == portId).IsEnabled);

        await using AppDbContext verification = db.CreateContext();
        PortRecord? record = await verification.Ports.FindAsync([portId], TestContext.Current.CancellationToken);
        Assert.NotNull(record);
        Assert.True(record.IsEnabled);
    }

    [Fact]
    public async Task ConnectAsync_WhenTheStartFails_ThrowsWithTheOriginalException()
    {
        Guid portId = Guid.NewGuid();
        InvalidOperationException failure = new("transport refused");
        PortServiceSessionController controller = new(
            new StubSessionRuntime(PortSessionStartResult.Failed(portId, failure)));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.ConnectAsync(portId, TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown.InnerException);
        Assert.Contains("Failed", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectAsync_WhenTheStartIsCancelled_RethrowsCancellation()
    {
        Guid portId = Guid.NewGuid();
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();
        OperationCanceledException cancellation = new(cts.Token);
        PortServiceSessionController controller = new(
            new StubSessionRuntime(PortSessionStartResult.Cancelled(portId, cancellation)));

        OperationCanceledException thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.ConnectAsync(portId, cts.Token));

        // The coordinator's reconnect loop keys off cancellation, so the original
        // exception must surface rather than a wrapped start failure.
        Assert.Same(cancellation, thrown);
    }

    private static PortConfig CreatePort(Guid portId, bool isEnabled) => new()
    {
        Id = portId,
        Name = "KISS",
        IsEnabled = isEnabled,
        IsRx = true,
        IsTx = true,
        TypeSettings = new KissSettings
        {
            Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
        }
    };

    private sealed class StubSessionRuntime(PortSessionStartResult start) : IPortSessionRuntime
    {
        public Task<PortSessionStartResult> StartSessionAsync(Guid portId, CancellationToken cancellationToken) =>
            Task.FromResult(start);

        public Task<PortSessionStopResult> StopSessionAsync(Guid portId, CancellationToken cancellationToken) =>
            Task.FromResult(PortSessionStopResult.NotRunning(portId));
    }

    private static PortService CreateService(TempAppDatabase db, IKissStreamFactory kissStreamFactory)
    {
        IDigipeaterService digipeater = Substitute.For<IDigipeaterService>();
        digipeater.GetDigipeatTargets(
            Arg.Any<AprsPacket>(),
            Arg.Any<Guid>(),
            Arg.Any<bool>(),
            Arg.Any<IReadOnlyList<PortInfo>>())
            .Returns([]);

        return TestPortService.Create(
            db,
            TestSessionManager.Create(TestModemFactory.Create(kissStreamFactory: kissStreamFactory)),
            new TestPortService.PortServiceCollaborators
            {
                PacketStorageService = Substitute.For<IPacketStorageService>(),
                DigipeaterService = digipeater,
                KeepAliveService = Substitute.For<IKeepAliveService>()
            });
    }

    private sealed class TrackingKissStreamFactory : IKissStreamFactory
    {
        private int _openCount;

        public IReadOnlyCollection<Type> SupportedTransports { get; } =
            [typeof(TcpKissTransportSettings)];

        public int OpenCount => Volatile.Read(ref _openCount);

        public int StreamDisposeCount { get => Volatile.Read(ref field); private set; }

        public Task<Stream> OpenAsync(
            KissSettings settings,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _openCount);
            return Task.FromResult<Stream>(new TrackedStream(this));
        }

        private sealed class TrackedStream(TrackingKissStreamFactory owner) : Stream
        {
            private int _disposed;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => 0;

            public override long Position
            {
                get => 0;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => 0;

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return 0;
                }

                return 0;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                MarkDisposed();
                base.Dispose(disposing);
            }

            private void MarkDisposed()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    Interlocked.Increment(ref owner.StreamDisposeCount);
                }
            }
        }
    }

}

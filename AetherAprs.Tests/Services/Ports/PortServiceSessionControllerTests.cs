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
using AetherAprs.Configuration.Settings;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using AetherAprs.Transports.Kiss;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

/// <summary>
/// Regression tests for audit C-02: supervision must start and stop modems
/// without rewriting the user's persisted <see cref="PortConfig.IsEnabled"/>.
/// </summary>
public sealed class PortServiceSessionControllerTests
{
    [Fact]
    public async Task ConnectAsync_StartsModemWithoutPersistingEnabledIntent()
    {
        var portId = Guid.NewGuid();
        var port = CreatePort(portId, isEnabled: false);
        using var db = TempAppDatabase.Create(port);
        var factory = new TrackingKissStreamFactory();
        await using var service = CreateService(db, factory);
        await service.InitializeAsync(TestContext.Current.CancellationToken);
        var controller = new PortServiceSessionController(service);

        await controller.ConnectAsync(portId, TestContext.Current.CancellationToken);

        Assert.Equal(1, factory.OpenCount);
        Assert.False(service.Ports.Single(p => p.Id == portId).IsEnabled);

        await using var verification = db.CreateContext();
        var record = await verification.Ports.FindAsync([portId], TestContext.Current.CancellationToken);
        Assert.NotNull(record);
        Assert.False(record.IsEnabled);

        await service.StopAllPortsAsync();
    }

    [Fact]
    public async Task DisconnectAsync_StopsModemWithoutClearingPersistedIntent()
    {
        var portId = Guid.NewGuid();
        var port = CreatePort(portId, isEnabled: true);
        using var db = TempAppDatabase.Create(port);
        var factory = new TrackingKissStreamFactory();
        await using var service = CreateService(db, factory);
        await service.InitializeAsync(TestContext.Current.CancellationToken);
        var controller = new PortServiceSessionController(service);

        await controller.ConnectAsync(portId, TestContext.Current.CancellationToken);
        await controller.DisconnectAsync(portId, TestContext.Current.CancellationToken);

        Assert.Equal(1, factory.OpenCount);
        Assert.Equal(1, factory.StreamDisposeCount);
        Assert.True(service.Ports.Single(p => p.Id == portId).IsEnabled);

        await using var verification = db.CreateContext();
        var record = await verification.Ports.FindAsync([portId], TestContext.Current.CancellationToken);
        Assert.NotNull(record);
        Assert.True(record.IsEnabled);
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

    private static PortService CreateService(TempAppDatabase db, IKissStreamFactory kissStreamFactory)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var digipeater = Substitute.For<IDigipeaterService>();
        digipeater.GetDigipeatTargets(
            Arg.Any<AprsPacket>(),
            Arg.Any<Guid>(),
            Arg.Any<bool>(),
            Arg.Any<IReadOnlyList<PortInfo>>())
            .Returns([]);

        return new PortService(
            db.Factory,
            new TestConfigurationService(),
            services.GetRequiredService<ILoggerFactory>(),
            NullLogger<PortService>.Instance,
            kissStreamFactory,
            Substitute.For<IPacketStorageService>(),
            Substitute.For<IKeepAliveService>(),
            digipeater,
            Substitute.For<IAudioStreamService>(),
            Substitute.For<IAudioPlaybackService>(),
            Substitute.For<IPttService>(),
            new NullSecretProtector());
    }

    private sealed class TrackingKissStreamFactory : IKissStreamFactory
    {
        private int _openCount;
        private int _disposeCount;

        public IReadOnlyCollection<Type> SupportedTransports { get; } =
            [typeof(TcpKissTransportSettings)];

        public int OpenCount => Volatile.Read(ref _openCount);

        public int StreamDisposeCount => Volatile.Read(ref _disposeCount);

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
                    Interlocked.Increment(ref owner._disposeCount);
                }
            }
        }
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
#pragma warning disable CS0067 // Event is never used in this test double
        public event EventHandler? SettingsChanged;
#pragma warning restore CS0067

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

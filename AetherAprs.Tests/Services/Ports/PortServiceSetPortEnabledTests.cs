// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class PortServiceSetPortEnabledTests
{
    [Fact]
    public async Task SetPortEnabledAsync_DisablingRunningPort_StopsModem()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var port = new PortConfig
        {
            Id = portId,
            Name = "KISS",
            IsEnabled = false,
            IsTx = true,
            TypeSettings = new KissSettings
            {
                Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
            }
        };
        using var db = TempAppDatabase.Create(port);
        var factory = new TrackingKissStreamFactory();
        var service = await CreateAndInitializeServiceAsync(db, factory);

        // Start the port
        await service.SetPortEnabledAsync(portId, true);
        Assert.Equal(1, factory.OpenCount);
        Assert.Equal(0, factory.StreamDisposeCount);

        // Act - disable the port
        await service.SetPortEnabledAsync(portId, false);

        // Assert - modem should be stopped and stream disposed
        Assert.Equal(1, factory.StreamDisposeCount);
        Assert.False(service.Ports.Single(p => p.Id == portId).IsEnabled);
    }

    private static PortService CreateService(
        TempAppDatabase db,
        IKissStreamFactory? kissStreamFactory = null)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        kissStreamFactory ??= new KissStreamFactory([new TcpKissStreamConnector()]);
        var mockPacketStorage = Substitute.For<IPacketStorageService>();
        var mockForegroundService = Substitute.For<IForegroundService>();
        var mockDigipeater = Substitute.For<IDigipeaterService>();
        mockDigipeater.GetDigipeatTargets(
            Arg.Any<AprsPacket>(),
            Arg.Any<Guid>(),
            Arg.Any<bool>(),
            Arg.Any<System.Collections.Generic.IReadOnlyList<PortInfo>>())
            .Returns([]);

        return new PortService(
            db.Factory,
            new TestConfigurationService(),
            services.GetRequiredService<ILoggerFactory>(),
            NullLogger<PortService>.Instance,
            kissStreamFactory,
            mockPacketStorage,
            mockForegroundService,
            mockDigipeater);
    }

    private static async Task<PortService> CreateAndInitializeServiceAsync(
        TempAppDatabase db,
        IKissStreamFactory? kissStreamFactory = null)
    {
        var service = CreateService(db, kissStreamFactory);
        await service.InitializeAsync(TestContext.Current.CancellationToken);
        return service;
    }

    private sealed class TrackingKissStreamFactory : IKissStreamFactory, IDisposable
    {
        private TrackingStream? _lastStream;

        public int OpenCount { get; private set; }
        public int StreamDisposeCount => _lastStream?.DisposeCount ?? 0;

        public System.Collections.Generic.IReadOnlyCollection<Type> SupportedTransports { get; } =
            [typeof(TcpKissTransportSettings)];

        public Task<Stream> OpenAsync(KissSettings settings, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            _lastStream = new TrackingStream();
            return Task.FromResult<Stream>(_lastStream);
        }

        public void Dispose()
        {
            _lastStream?.Dispose();
        }
    }

    private sealed class TrackingStream : MemoryStream
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }
            base.Dispose(disposing);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
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

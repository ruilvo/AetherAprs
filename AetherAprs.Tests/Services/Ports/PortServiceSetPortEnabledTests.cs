// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;
using AetherAprs.Transports.Kiss;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class PortServiceSetPortEnabledTests
{
    [Fact]
    public async Task SetPortEnabledAsync_DisablingRunningPort_StopsModem()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        PortConfig port = new()
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
        using TempAppDatabase db = TempAppDatabase.Create(port);
        TrackingKissStreamFactory factory = new();
        PortService service = await CreateAndInitializeServiceAsync(db, factory);

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

    [Fact]
    public async Task SetPortEnabledAsync_EnablingPort_RequestsThePortsPermissions()
    {
        Guid portId = Guid.NewGuid();
        PortConfig port = new()
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
        using TempAppDatabase db = TempAppDatabase.Create(port);
        IPortPermissionPrompter prompter = Substitute.For<IPortPermissionPrompter>();
        prompter.RequestForPortAsync(Arg.Any<PortConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        PortService service = await CreateAndInitializeServiceAsync(
            db,
            new TrackingKissStreamFactory(),
            prompter);

        await service.SetPortEnabledAsync(portId, true);

        await prompter.Received(1).RequestForPortAsync(
            Arg.Is<PortConfig>(candidate => candidate.Id == portId),
            Arg.Any<CancellationToken>());

        await service.SetPortEnabledAsync(portId, false);
    }

    [Fact]
    public async Task SetPortEnabledAsync_PermissionPromptFails_StillEnablesThePort()
    {
        Guid portId = Guid.NewGuid();
        PortConfig port = new()
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
        using TempAppDatabase db = TempAppDatabase.Create(port);
        IPortPermissionPrompter prompter = Substitute.For<IPortPermissionPrompter>();
        prompter.RequestForPortAsync(Arg.Any<PortConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("prompt host is gone")));
        PortService service = await CreateAndInitializeServiceAsync(
            db,
            new TrackingKissStreamFactory(),
            prompter);

        await service.SetPortEnabledAsync(portId, true);

        Assert.True(service.Ports.Single(p => p.Id == portId).IsEnabled);

        await service.SetPortEnabledAsync(portId, false);
    }

    private static PortService CreateService(
        TempAppDatabase db,
        IKissStreamFactory? kissStreamFactory = null,
        IPortPermissionPrompter? permissionPrompter = null)
    {
        kissStreamFactory ??= new KissStreamFactory([new TcpKissStreamConnector()]);
        IPacketStorageService mockPacketStorage = Substitute.For<IPacketStorageService>();
        IKeepAliveService mockKeepAliveService = Substitute.For<IKeepAliveService>();
        IDigipeaterService mockDigipeater = Substitute.For<IDigipeaterService>();
        mockDigipeater.GetDigipeatTargets(
            Arg.Any<AprsPacket>(),
            Arg.Any<Guid>(),
            Arg.Any<bool>(),
            Arg.Any<System.Collections.Generic.IReadOnlyList<PortInfo>>())
            .Returns([]);

        return TestPortService.Create(
            db,
            TestSessionManager.Create(TestModemFactory.Create(kissStreamFactory: kissStreamFactory)),
            new TestPortService.PortServiceCollaborators
            {
                PacketStorageService = mockPacketStorage,
                DigipeaterService = mockDigipeater,
                KeepAliveService = mockKeepAliveService,
                PermissionPrompter = permissionPrompter
            });
    }

    private static async Task<PortService> CreateAndInitializeServiceAsync(
        TempAppDatabase db,
        IKissStreamFactory? kissStreamFactory = null,
        IPortPermissionPrompter? permissionPrompter = null)
    {
        PortService service = CreateService(db, kissStreamFactory, permissionPrompter);
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

        public void Dispose() => _lastStream?.Dispose();
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

}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
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

public sealed class PortServiceAfskStartTests
{
    [Fact]
    public async Task SetPortEnabledAsync_EnablingAfskPort_StartsAudioCapture()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var port = new PortConfig
        {
            Id = portId,
            Name = "AFSK",
            IsEnabled = false,
            IsTx = false, // Slice A is receive-only
            TypeSettings = new AfskSettings()
        };
        using var db = TempAppDatabase.Create(port);
        var audioStream = Substitute.For<IAudioStreamService>();
        audioStream.SampleRate.Returns(48000);
        var service = await CreateAndInitializeServiceAsync(db, audioStream);

        // Act
        await service.SetPortEnabledAsync(portId, true);

        // Assert
        audioStream.Received(1).StartCapture();
        Assert.True(service.Ports.Single(p => p.Id == portId).IsEnabled);

        // Cleanup
        await service.StopAllPortsAsync();
    }

    [Fact]
    public async Task SetPortEnabledAsync_DisablingAfskPort_StopsAudioCapture()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var port = new PortConfig
        {
            Id = portId,
            Name = "AFSK",
            IsEnabled = false,
            IsTx = false,
            TypeSettings = new AfskSettings()
        };
        using var db = TempAppDatabase.Create(port);
        var audioStream = Substitute.For<IAudioStreamService>();
        audioStream.SampleRate.Returns(48000);
        var service = await CreateAndInitializeServiceAsync(db, audioStream);

        // Start the port first so an active session exists.
        await service.SetPortEnabledAsync(portId, true);
        audioStream.ClearReceivedCalls();

        // Act
        await service.SetPortEnabledAsync(portId, false);

        // Assert
        audioStream.Received(1).StopCapture();
        Assert.False(service.Ports.Single(p => p.Id == portId).IsEnabled);
    }

    [Fact]
    public async Task InitializeAsync_AfskPort_RoundTripsTypeSettings()
    {
        // Arrange
        var portId = Guid.NewGuid();
        var port = new PortConfig
        {
            Id = portId,
            Name = "AFSK",
            TypeSettings = new AfskSettings()
        };
        using var db = TempAppDatabase.Create(port);
        var audioStream = Substitute.For<IAudioStreamService>();
        audioStream.SampleRate.Returns(48000);

        // Act
        var service = await CreateAndInitializeServiceAsync(db, audioStream);

        // Assert - the AFSK type settings survive the DB round-trip.
        var persisted = Assert.Single(service.Ports);
        Assert.IsType<AfskSettings>(persisted.TypeSettings);
    }

    private static PortService CreateService(TempAppDatabase db, IAudioStreamService audioStream)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var mockPacketStorage = Substitute.For<IPacketStorageService>();
        var mockKeepAliveService = Substitute.For<IKeepAliveService>();
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
            new KissStreamFactory([new TcpKissStreamConnector()]),
            mockPacketStorage,
            mockKeepAliveService,
            mockDigipeater,
            audioStream,
            Substitute.For<IAudioPlaybackService>(),
            Substitute.For<IPttService>());
    }

    private static async Task<PortService> CreateAndInitializeServiceAsync(
        TempAppDatabase db,
        IAudioStreamService audioStream)
    {
        var service = CreateService(db, audioStream);
        await service.InitializeAsync(TestContext.Current.CancellationToken);
        return service;
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

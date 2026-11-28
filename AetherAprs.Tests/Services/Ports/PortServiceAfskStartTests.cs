// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;
using System.Threading.Tasks;

using AetherAprs.Configuration;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public sealed class PortServiceAfskStartTests
{
    [Fact]
    public async Task SetPortEnabledAsync_EnablingAfskPort_StartsAudioCapture()
    {
        // Arrange
        Guid portId = Guid.NewGuid();
        PortConfig port = new()
        {
            Id = portId,
            Name = "AFSK",
            IsEnabled = false,
            IsTx = false, // Slice A is receive-only
            TypeSettings = new AfskSettings()
        };
        using TempAppDatabase db = TempAppDatabase.Create(port);
        IAudioStreamService audioStream = Substitute.For<IAudioStreamService>();
        audioStream.SampleRate.Returns(48000);
        PortService service = await CreateAndInitializeServiceAsync(db, audioStream);

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
        Guid portId = Guid.NewGuid();
        PortConfig port = new()
        {
            Id = portId,
            Name = "AFSK",
            IsEnabled = false,
            IsTx = false,
            TypeSettings = new AfskSettings()
        };
        using TempAppDatabase db = TempAppDatabase.Create(port);
        IAudioStreamService audioStream = Substitute.For<IAudioStreamService>();
        audioStream.SampleRate.Returns(48000);
        PortService service = await CreateAndInitializeServiceAsync(db, audioStream);

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
        Guid portId = Guid.NewGuid();
        PortConfig port = new()
        {
            Id = portId,
            Name = "AFSK",
            TypeSettings = new AfskSettings()
        };
        using TempAppDatabase db = TempAppDatabase.Create(port);
        IAudioStreamService audioStream = Substitute.For<IAudioStreamService>();
        audioStream.SampleRate.Returns(48000);

        // Act
        PortService service = await CreateAndInitializeServiceAsync(db, audioStream);

        // Assert - the AFSK type settings survive the DB round-trip.
        PortConfig persisted = Assert.Single(service.Ports);
        Assert.IsType<AfskSettings>(persisted.TypeSettings);
    }

    private static PortService CreateService(TempAppDatabase db, IAudioStreamService audioStream)
    {
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
            TestSessionManager.Create(TestModemFactory.Create(audioStreamService: audioStream)),
            new TestPortService.PortServiceCollaborators
            {
                PacketStorageService = mockPacketStorage,
                DigipeaterService = mockDigipeater,
                KeepAliveService = mockKeepAliveService
            });
    }

    private static async Task<PortService> CreateAndInitializeServiceAsync(
        TempAppDatabase db,
        IAudioStreamService audioStream)
    {
        PortService service = CreateService(db, audioStream);
        await service.InitializeAsync(TestContext.Current.CancellationToken);
        return service;
    }

}

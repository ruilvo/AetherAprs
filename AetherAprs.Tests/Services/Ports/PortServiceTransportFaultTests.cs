// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
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

/// <summary>
/// The session owner must attribute a modem fault to the port that raised it
/// without matching on the event sender instance.
/// </summary>
public sealed class PortServiceTransportFaultTests
{
    [Fact]
    public async Task ReceiveError_RaisesTransportFaultForTheOwningPort()
    {
        Guid portId = Guid.NewGuid();
        PortConfig port = new()
        {
            Id = portId,
            Name = "Scripted",
            IsEnabled = true,
            TypeSettings = new KissSettings
            {
                Transport = new TcpKissTransportSettings { Host = "127.0.0.1", Port = 8001 }
            }
        };

        using TempAppDatabase db = TempAppDatabase.Create(port);
        ScriptedPortModem modem = new();
        await using PortService service = CreateService(db, modem);
        await service.InitializeAsync(TestContext.Current.CancellationToken);

        TaskCompletionSource<PortTransportFaultEventArgs> reported = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.TransportFaulted += (_, args) => reported.TrySetResult(args);

        await service.SetPortEnabledAsync(portId, true);

        InvalidOperationException expected = new("socket lost");
        modem.RaiseReceiveError(expected);

        PortTransportFaultEventArgs args = await reported.Task.WaitAsync(
            TestTimeouts.TestTimeout,
            TestContext.Current.CancellationToken);

        Assert.Equal(portId, args.PortId);
        Assert.Same(expected, args.Exception);
    }

    [Fact]
    public async Task StopAllPortsAsync_DisposesTheModemItStarted()
    {
        Guid portId = Guid.NewGuid();
        PortConfig port = new()
        {
            Id = portId,
            Name = "Scripted",
            IsEnabled = true,
            TypeSettings = new AfskSettings()
        };

        using TempAppDatabase db = TempAppDatabase.Create(port);
        ScriptedPortModem modem = new();
        await using PortService service = CreateService(db, modem);
        await service.InitializeAsync(TestContext.Current.CancellationToken);

        await service.SetPortEnabledAsync(portId, true);
        await service.StopAllPortsAsync();

        Assert.Equal(1, modem.DisposeCount);
        Assert.Equal(1, modem.StopCount);
    }

    private static PortService CreateService(TempAppDatabase db, IPortModem modem)
    {
        IDigipeaterService digipeater = Substitute.For<IDigipeaterService>();
        digipeater.GetDigipeatTargets(
            Arg.Any<AprsPacket>(),
            Arg.Any<Guid>(),
            Arg.Any<bool>(),
            Arg.Any<System.Collections.Generic.IReadOnlyList<PortInfo>>())
            .Returns([]);

        return TestPortService.Create(
            db,
            TestSessionManager.Create(new SingleModemFactory(modem)),
            new TestPortService.PortServiceCollaborators
            {
                PacketStorageService = Substitute.For<IPacketStorageService>(),
                DigipeaterService = digipeater,
                KeepAliveService = Substitute.For<IKeepAliveService>()
            });
    }

    private sealed class SingleModemFactory(IPortModem modem) : IModemFactory
    {
        public Task<IPortModem> CreateAsync(
            PortConfig port,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(modem);
    }

    private sealed class ScriptedPortModem : IPortModem
    {
        public string TransportName => "Scripted";

        public event EventHandler<AprsPacket>? PacketReceived;

        public event EventHandler<Exception>? ReceiveError;

        public int StopCount { get; private set; }

        public int DisposeCount { get; private set; }

        public void Start()
        {
        }

        public Task StopAsync()
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public Task SendAsync(AprsPacket packet, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public void RaiseReceiveError(Exception exception) => ReceiveError?.Invoke(this, exception);

        public void RaisePacketReceived(AprsPacket packet) => PacketReceived?.Invoke(this, packet);
    }
}

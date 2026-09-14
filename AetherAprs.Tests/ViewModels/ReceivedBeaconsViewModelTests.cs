// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.ViewModels;
using Mapsui.Layers;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class ReceivedBeaconsViewModelTests
{
    [Fact]
    public async Task HiddenPortKeepsHistoryAndRestoresOnShow()
    {
        var port = new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "IS",
            Type = PortType.AprsIs,
            ShowOnMap = true,
            TypeSettings = new AprsIsSettings()
        };
        var portService = new TestPortService(port);
        using var symbols = new TestSymbolBitmapProvider();
        using var viewModel = new ReceivedBeaconsViewModel(portService, symbols);

        portService.RaisePacket(port.Id, CreatePosition("N0CALL-1", 38.7, -9.1));
        await WaitForAsync(() => CountFeatures(viewModel) == 1);

        port.ShowOnMap = false;
        portService.RaisePortsChanged();
        await WaitForAsync(() => CountFeatures(viewModel) == 0);

        port.ShowOnMap = true;
        portService.RaisePortsChanged();
        await WaitForAsync(() => CountFeatures(viewModel) == 1);
    }

    [Fact]
    public async Task PacketsOnHiddenPortAreBufferedUntilShown()
    {
        var port = new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "IS",
            Type = PortType.AprsIs,
            ShowOnMap = false,
            TypeSettings = new AprsIsSettings()
        };
        var portService = new TestPortService(port);
        using var symbols = new TestSymbolBitmapProvider();
        using var viewModel = new ReceivedBeaconsViewModel(portService, symbols);

        portService.RaisePacket(port.Id, CreatePosition("N0CALL-2", 40.0, -8.0));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(0, CountFeatures(viewModel));

        port.ShowOnMap = true;
        portService.RaisePortsChanged();
        await WaitForAsync(() => CountFeatures(viewModel) == 1);
    }

    private static int CountFeatures(ReceivedBeaconsViewModel viewModel)
    {
        return viewModel.BeaconsLayer is WritableLayer layer
            ? layer.GetFeatures().Count()
            : 0;
    }

    private static PositionPacket CreatePosition(string callsignSsid, double lat, double lon)
    {
        var parts = callsignSsid.Split('-');
        var callsign = new Callsign(parts[0], int.Parse(parts[1]));
        return new PositionPacket
        {
            Source = callsign,
            Destination = new Callsign("APRS"),
            Latitude = lat,
            Longitude = lon,
            Symbol = new Symbol('/'.ToSymbolTable(), '['.ToSymbolCode())
        };
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condition(), "Condition was not met before timeout.");
    }

    private sealed class TestPortService : IPortService
    {
        public TestPortService(PortConfig port)
        {
            Ports = new List<PortConfig> { port };
        }

        public IReadOnlyList<PortConfig> Ports { get; }

        public event EventHandler? PortsChanged;

        public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;

        public void RaisePortsChanged() => PortsChanged?.Invoke(this, EventArgs.Empty);

        public void RaisePacket(Guid portId, AprsPacket packet)
        {
            PacketReceived?.Invoke(this, new PortPacketReceivedEventArgs
            {
                PortId = portId,
                Packet = packet
            });
        }

        public Task AddPortAsync(PortConfig port) => Task.CompletedTask;

        public Task UpdatePortAsync(PortConfig port) => Task.CompletedTask;

        public Task RemovePortAsync(Guid id) => Task.CompletedTask;

        public Task SetPortEnabledAsync(Guid id, bool enabled) => Task.CompletedTask;

        public Task SetPortShowOnMapAsync(Guid id, bool showOnMap) => Task.CompletedTask;

        public Task SendPacketAsync(Guid id, AprsPacket packet) => Task.CompletedTask;

        public Task StartAllEnabledPortsAsync() => Task.CompletedTask;

        public Task StopAllPortsAsync() => Task.CompletedTask;
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider
    {
        public SKBitmap GetSymbolBitmap(Symbol symbol)
        {
            return new SKBitmap(16, 16);
        }

        public void Dispose()
        {
        }
    }
}

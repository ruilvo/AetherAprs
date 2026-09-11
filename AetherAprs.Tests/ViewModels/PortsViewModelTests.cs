// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.ViewModels;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class PortsViewModelTests
{
    [Fact]
    public void ConstructorLoadsPortItemsAndCalculatesNextAprsIsNumber()
    {
        var first = CreatePort(PortType.AprsIs, "First");
        var second = CreatePort(PortType.Kiss, "Second");
        var portService = new TestPortService(first, second);
        var viewModel = new PortsViewModel(portService, new TestConfigurationService());

        Assert.Collection(
            viewModel.PortItems,
            item => Assert.Equal("First", item.Name),
            item => Assert.Equal("Second", item.Name));
        Assert.Equal(2, viewModel.GetNextPortNumber());
    }

    [Fact]
    public void PortsChangedReloadsPortItems()
    {
        var original = CreatePort(PortType.AprsIs, "Original");
        var replacement = CreatePort(PortType.AprsIs, "Replacement");
        var portService = new TestPortService(original);
        var viewModel = new PortsViewModel(portService, new TestConfigurationService());

        portService.ReplacePorts(replacement);

        var item = Assert.Single(viewModel.PortItems);
        Assert.Equal("Replacement", item.Name);
    }

    [Fact]
    public async Task TogglingPortForwardsEnabledState()
    {
        var port = CreatePort(PortType.AprsIs, "Port");
        var portService = new TestPortService(port);
        var viewModel = new PortsViewModel(portService, new TestConfigurationService());
        var item = Assert.Single(viewModel.PortItems);

        item.IsEnabled = true;
        await portService.LastOperation;

        Assert.Equal(port.Id, portService.LastPortId);
        Assert.True(portService.LastEnabled);
    }

    private static PortConfig CreatePort(PortType type, string name) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Name = name,
        SymbolTableCharacter = "\\",
        SymbolCodeCharacter = ">"
    };

    private sealed class TestPortService : IPortService
    {
        private readonly List<PortConfig> _ports;
        private TaskCompletionSource<bool> _operation = NewOperation();

        public TestPortService(params PortConfig[] ports) => _ports = [.. ports];
        public IReadOnlyList<PortConfig> Ports => _ports;
        public event EventHandler? PortsChanged;
        public event EventHandler<AprsPacket>? PacketReceived;
        public Guid? LastPortId { get; private set; }
        public bool LastEnabled { get; private set; }
        public Task LastOperation => _operation.Task;

        public void ReplacePorts(params PortConfig[] ports)
        {
            _ports.Clear();
            _ports.AddRange(ports);
            PortsChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task SetPortEnabledAsync(Guid id, bool enabled)
        {
            LastPortId = id;
            LastEnabled = enabled;
            _operation.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task AddPortAsync(PortConfig port) => Task.CompletedTask;
        public Task UpdatePortAsync(PortConfig port) => Task.CompletedTask;
        public Task RemovePortAsync(Guid id) => Task.CompletedTask;
        public Task SendPacketAsync(Guid id, AprsPacket packet) => Task.CompletedTask;
        public Task StartAllEnabledPortsAsync() => Task.CompletedTask;
        public Task StopAllPortsAsync() => Task.CompletedTask;

        private static TaskCompletionSource<bool> NewOperation() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
        public AppSettings Settings { get; } = new();
        public Task SaveSettingsAsync() => Task.CompletedTask;
    }
}

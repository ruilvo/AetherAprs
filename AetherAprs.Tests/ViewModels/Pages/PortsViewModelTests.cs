// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Localization;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Ports;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class PortsViewModelTests
{
    [Fact]
    public void ConstructorLoadsPortItemsAndCalculatesNextAprsIsNumber()
    {
        PortConfig first = CreatePort(new AprsIsSettings(), "First");
        PortConfig second = CreatePort(new KissSettings { Transport = new TcpKissTransportSettings() }, "Second");
        TestPortService portService = new(first, second);
        INavigationService navService = Substitute.For<INavigationService>();
        IAddEditPortViewModelFactory factory = Substitute.For<IAddEditPortViewModelFactory>();
        PortsViewModel viewModel = new(portService, new TestConfigurationService(), navService, factory, new TestConfirmationDialogService(), NullLogger<PortsViewModel>.Instance, NullLoggerFactory.Instance);

        Assert.Collection(
            viewModel.PortItems,
            item => Assert.Equal("First", item.Name),
            item => Assert.Equal("Second", item.Name));

        // Two ports are configured, so the next free number is three.
        Assert.Equal(3, viewModel.GetNextPortNumber());
    }

    [Fact]
    public void GetNextPortNumber_UsesEveryPortType_NotOnlyAprsIs()
    {
        // Names come from the localized default template, not from a hardcoded English
        // string, so the test does not depend on the culture another test left behind.
        PortConfig kissOne = CreatePort(
            new KissSettings { Transport = new TcpKissTransportSettings() },
            Strings.Format("DefaultKissPortName", 1));
        PortConfig kissTwo = CreatePort(
            new KissSettings { Transport = new TcpKissTransportSettings() },
            Strings.Format("DefaultKissPortName", 2));
        TestPortService portService = new(kissOne, kissTwo);
        using PortsViewModel viewModel = new(
            portService,
            new TestConfigurationService(),
            Substitute.For<INavigationService>(),
            Substitute.For<IAddEditPortViewModelFactory>(),
            new TestConfirmationDialogService(),
            NullLogger<PortsViewModel>.Instance,
            NullLoggerFactory.Instance);

        // Counting APRS-IS ports alone returned 1 here, which duplicated "KISS Port 1".
        Assert.Equal(3, viewModel.GetNextPortNumber());
    }

    [Fact]
    public void GetNextPortNumber_DoesNotReuseANumberAfterAPortIsDeleted()
    {
        PortConfig kissOne = CreatePort(
            new KissSettings { Transport = new TcpKissTransportSettings() },
            Strings.Format("DefaultKissPortName", 1));
        PortConfig kissThree = CreatePort(
            new KissSettings { Transport = new TcpKissTransportSettings() },
            Strings.Format("DefaultKissPortName", 3));
        TestPortService portService = new(kissOne, kissThree);
        using PortsViewModel viewModel = new(
            portService,
            new TestConfigurationService(),
            Substitute.For<INavigationService>(),
            Substitute.For<IAddEditPortViewModelFactory>(),
            new TestConfirmationDialogService(),
            NullLogger<PortsViewModel>.Instance,
            NullLoggerFactory.Instance);

        Assert.Equal(4, viewModel.GetNextPortNumber());
    }

    [Fact]
    public void PortsChangedReloadsPortItems()
    {
        PortConfig original = CreatePort(new AprsIsSettings(), "Original");
        PortConfig replacement = CreatePort(new AprsIsSettings(), "Replacement");
        TestPortService portService = new(original);
        INavigationService navService = Substitute.For<INavigationService>();
        IAddEditPortViewModelFactory factory = Substitute.For<IAddEditPortViewModelFactory>();
        PortsViewModel viewModel = new(portService, new TestConfigurationService(), navService, factory, new TestConfirmationDialogService(), NullLogger<PortsViewModel>.Instance, NullLoggerFactory.Instance);

        portService.ReplacePorts(replacement);

        PortItemViewModel item = Assert.Single(viewModel.PortItems);
        Assert.Equal("Replacement", item.Name);
    }

    [Fact]
    public void PortsChangedUpdatesEmptyState()
    {
        TestPortService portService = new();
        using PortsViewModel viewModel = new(
            portService, new TestConfigurationService(), Substitute.For<INavigationService>(),
            Substitute.For<IAddEditPortViewModelFactory>(), new TestConfirmationDialogService(),
            NullLogger<PortsViewModel>.Instance, NullLoggerFactory.Instance);
        int changes = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PortsViewModel.HasPorts))
                changes++;
        };

        Assert.False(viewModel.HasPorts);
        portService.ReplacePorts(CreatePort(new AprsIsSettings(), "Test"));
        Assert.True(viewModel.HasPorts);
        portService.ReplacePorts();
        Assert.False(viewModel.HasPorts);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task TogglingPortForwardsEnabledState()
    {
        PortConfig port = CreatePort(new AprsIsSettings(), "Port");
        TestPortService portService = new(port);
        INavigationService navService = Substitute.For<INavigationService>();
        IAddEditPortViewModelFactory factory = Substitute.For<IAddEditPortViewModelFactory>();
        PortsViewModel viewModel = new(portService, new TestConfigurationService(), navService, factory, new TestConfirmationDialogService(), NullLogger<PortsViewModel>.Instance, NullLoggerFactory.Instance);
        PortItemViewModel item = Assert.Single(viewModel.PortItems);

        item.IsEnabled = true;
        await portService.LastOperation;

        Assert.Equal(port.Id, portService.LastPortId);
        Assert.True(portService.LastEnabled);
    }

    [Fact]
    public async Task DeletingPortWhenConfirmedRemovesPort()
    {
        PortConfig port = CreatePort(new AprsIsSettings(), "Port");
        TestPortService portService = new(port);
        INavigationService navService = Substitute.For<INavigationService>();
        IAddEditPortViewModelFactory factory = Substitute.For<IAddEditPortViewModelFactory>();
        TestConfirmationDialogService dialogService = new() { Confirmed = true };
        PortsViewModel viewModel = new(portService, new TestConfigurationService(), navService, factory, dialogService, NullLogger<PortsViewModel>.Instance, NullLoggerFactory.Instance);
        PortItemViewModel item = Assert.Single(viewModel.PortItems);

        await item.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogService.CallCount);
        Assert.Equal(port.Id, portService.LastRemovedPortId);
    }

    [Fact]
    public async Task DeletingPortWhenCancelledDoesNotRemovePort()
    {
        PortConfig port = CreatePort(new AprsIsSettings(), "Port");
        TestPortService portService = new(port);
        INavigationService navService = Substitute.For<INavigationService>();
        IAddEditPortViewModelFactory factory = Substitute.For<IAddEditPortViewModelFactory>();
        TestConfirmationDialogService dialogService = new() { Confirmed = false };
        PortsViewModel viewModel = new(portService, new TestConfigurationService(), navService, factory, dialogService, NullLogger<PortsViewModel>.Instance, NullLoggerFactory.Instance);
        PortItemViewModel item = Assert.Single(viewModel.PortItems);

        await item.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogService.CallCount);
        Assert.Null(portService.LastRemovedPortId);
    }

    private static PortConfig CreatePort(IPortTypeSettings typeSettings, string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        TypeSettings = typeSettings
    };

    private sealed class TestPortService(params PortConfig[] ports) : IPortService
    {
        private readonly List<PortConfig> _ports = [.. ports];
        private readonly TaskCompletionSource<bool> _operation = NewOperation();

        public IReadOnlyList<PortConfig> Ports => _ports;
        public event EventHandler? PortsChanged;
#pragma warning disable CS0067 // Event is never used - this is a test stub
        public event EventHandler<PortPacketReceivedEventArgs>? PacketReceived;
#pragma warning restore CS0067
        public Guid? LastPortId { get; private set; }
        public bool LastEnabled { get; private set; }
        public Guid? LastRemovedPortId { get; private set; }
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

        public Task SetPortShowOnMapAsync(Guid id, bool showOnMap) => Task.CompletedTask;

        public Task AddPortAsync(PortConfig port) => Task.CompletedTask;
        public Task UpdatePortAsync(PortConfig port) => Task.CompletedTask;
        public Task RemovePortAsync(Guid id)
        {
            LastRemovedPortId = id;
            return Task.CompletedTask;
        }
        public Task SendPacketAsync(Guid id, AprsPacket packet) => Task.CompletedTask;
        public Task StopAllPortsAsync() => Task.CompletedTask;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static TaskCompletionSource<bool> NewOperation() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
#pragma warning disable CS0067 // Event is never used in this test double
        public event EventHandler? SettingsChanged;
#pragma warning restore CS0067
        public AppSettings Current { get; set; } = new();

        public Task<AppSettings> UpdateAsync(
            Func<AppSettings, AppSettings> update,
            CancellationToken cancellationToken = default)
        {
            Current = update(Current);
            return Task.FromResult(Current);
        }
    }

    private sealed class TestConfirmationDialogService : IConfirmationDialogService
    {
        public bool Confirmed { get; init; } = true;
        public int CallCount { get; private set; }

        public Task<bool> ShowConfirmationAsync(string title, string message)
        {
            CallCount++;
            return Task.FromResult(Confirmed);
        }
    }
}

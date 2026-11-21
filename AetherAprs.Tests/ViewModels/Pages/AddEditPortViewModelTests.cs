// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Services.Ports;
using AetherAprs.Services.UI;
using AetherAprs.Transports.Kiss;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Pages;

public sealed class AddEditPortViewModelTests : TestFixtureBase
{
    [Fact]
    public async Task BleScanDoesNotReplaceRepeatedAdvertisementItems()
    {
        var scanner = Substitute.For<IBluetoothLeScanner>();
        scanner.EnsurePermissionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        scanner.ScanAsync(Arg.Any<CancellationToken>())
            .Returns(_ => RepeatAdvertisementAsync(_.Arg<CancellationToken>()));

        var viewModel = CreateViewModel(scanner);
        var scanTask = viewModel.StartBleScanCommand.ExecuteAsync(null);

        while (viewModel.BleDevices.Count < 1)
        {
            await Task.Yield();
        }

        var selected = viewModel.BleDevices[0];
        viewModel.SelectedBleDevice = selected;

        while (viewModel.IsBleScanning is false)
        {
            await Task.Yield();
        }

        Assert.Single(viewModel.BleDevices);
        Assert.Same(selected, viewModel.BleDevices[0]);
        Assert.Same(selected, viewModel.SelectedBleDevice);

        viewModel.StopBleScanCommand.Execute(null);
        await scanTask;
    }

    [Fact]
    public void BuildConfigUsesLoRaAprsTrackerBleProfile()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedPortSettingsType = typeof(KissSettings);
        viewModel.SelectedKissTransportType = typeof(BluetoothLeKissTransportSettings);

        var device = new BluetoothLeAdvertisement(
            "AA:BB:CC:DD:EE:FF",
            "LilyGO T-Echo Plus",
            -45,
            [BluetoothLeKissTransportSettings.LoRaAprsTrackerServiceUuid]);
        viewModel.BleDevices.Add(device);
        viewModel.SelectedBleDevice = device;

        var settings = Assert.IsType<KissSettings>(viewModel.BuildConfig().TypeSettings);
        var ble = Assert.IsType<BluetoothLeKissTransportSettings>(settings.Transport);

        Assert.Equal(BluetoothLeKissTransportSettings.LoRaAprsTrackerServiceUuid, ble.ServiceUuid);
        Assert.Equal(BluetoothLeKissTransportSettings.LoRaAprsTrackerRxCharacteristicUuid, ble.RxCharacteristicUuid);
        Assert.Equal(BluetoothLeKissTransportSettings.LoRaAprsTrackerTxCharacteristicUuid, ble.TxCharacteristicUuid);
    }

    [Fact]
    public void DefaultsEnableTransmit()
    {
        var viewModel = CreateViewModel();
        Assert.True(viewModel.IsTx);

        var config = viewModel.BuildConfig();
        Assert.True(config.IsTx);
    }

    [Fact]
    public void PopulateFromAprsIsConfig()
    {
        var viewModel = CreateViewModel();
        var config = new PortConfig
        {
            Name = "Test Port",
            TypeSettings = new AprsIsSettings
            {
                Server = "example.com",
                ServerPort = 14580,
                Passcode = "12345",
                Filter = "r/50/0/100"
            },
            IsRx = true,
            IsTx = false,
            ShowOnMap = true
        };

        viewModel.PopulateFrom(config);

        Assert.Equal("Test Port", viewModel.Name);
        Assert.Equal(typeof(AprsIsSettings), viewModel.SelectedPortSettingsType);
        Assert.Equal("example.com", viewModel.Server);
        Assert.Equal(14580, viewModel.ServerPort);
        Assert.Equal("12345", viewModel.Passcode);
        Assert.Equal(string.Empty, viewModel.FilterBuilder.Filter);
        Assert.True(viewModel.IsRx);
        Assert.False(viewModel.IsTx);
        Assert.True(viewModel.ShowOnMap);
    }

    [Fact]
    public void PopulateFromKissTcpConfig()
    {
        var viewModel = CreateViewModel();
        var config = new PortConfig
        {
            Name = "KISS Port",
            TypeSettings = new KissSettings
            {
                Transport = new TcpKissTransportSettings
                {
                    Host = "192.168.1.100",
                    Port = 8001
                }
            }
        };

        viewModel.PopulateFrom(config);

        Assert.Equal("KISS Port", viewModel.Name);
        Assert.Equal(typeof(KissSettings), viewModel.SelectedPortSettingsType);
        Assert.Equal(typeof(TcpKissTransportSettings), viewModel.SelectedKissTransportType);
        Assert.Equal("192.168.1.100", viewModel.TcpHost);
        Assert.Equal(8001, viewModel.TcpPort);
    }

    [Fact]
    public void InitializeWithoutExistingConfigKeepsAddPortTitle()
    {
        var viewModel = CreateViewModel(initialize: false);

        viewModel.Initialize("N0CALL", 1);

        Assert.False(viewModel.IsEditing);
        Assert.Equal("APRS-IS Port 1", viewModel.Name);
        Assert.Equal("Add Port", viewModel.Title);
    }

    [Fact]
    public void InitializeWithExistingConfigUsesEditTitle()
    {
        var viewModel = CreateViewModel(initialize: false);
        var existing = new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "Existing Port",
            TypeSettings = new AprsIsSettings()
        };

        viewModel.Initialize("N0CALL", 1, existing);

        Assert.True(viewModel.IsEditing);
        Assert.Equal("Edit Existing Port", viewModel.Title);
    }

    private static async IAsyncEnumerable<BluetoothLeAdvertisement> RepeatAdvertisementAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var advertisement = new BluetoothLeAdvertisement(
            "AA:BB:CC:DD:EE:FF",
            "Test BLE",
            -40,
            Array.Empty<Guid>());
        yield return advertisement;
        yield return advertisement with { Rssi = -60 };
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static AddEditPortViewModel CreateViewModel(
        IBluetoothLeScanner? bleScanner = null,
        bool initialize = true)
    {
        var nav = Substitute.For<INavigationService>();
        var portService = Substitute.For<IPortService>();
        var kissFactory = new KissStreamFactory(new List<IKissStreamConnector>());
        bleScanner ??= Substitute.For<IBluetoothLeScanner>();
        var btClassic = Substitute.For<IBluetoothClassicDeviceProvider>();
        
        var vm = new AddEditPortViewModel(nav, portService, kissFactory, bleScanner, btClassic, new AprsIsFilterBuilderViewModel());
        if (initialize)
        {
            vm.Initialize("N0CALL", 1);
        }
        return vm;
    }
}

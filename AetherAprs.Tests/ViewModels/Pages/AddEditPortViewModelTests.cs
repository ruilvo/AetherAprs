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

        await WaitUntilAsync(
            () => viewModel.BleDevices.Count >= 1,
            "the BLE advertisement to arrive");

        var selected = viewModel.BleDevices[0];
        viewModel.SelectedBleDevice = selected;

        await WaitUntilAsync(
            () => viewModel.IsBleScanning,
            "BLE scanning to start");

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

    [Fact]
    public async Task SaveFailure_ShowsGeneralError_AndKeepsBluetoothStatusClean()
    {
        var nav = Substitute.For<INavigationService>();
        var portService = Substitute.For<IPortService>();
        portService
            .When(service => service.AddPortAsync(Arg.Any<PortConfig>()))
            .Do(_ => throw new InvalidOperationException("storage unavailable"));
        var viewModel = new AddEditPortViewModel(
            nav,
            portService,
            new KissStreamFactory(new List<IKissStreamConnector>()),
            Substitute.For<IBluetoothLeScanner>(),
            Substitute.For<IBluetoothClassicDeviceProvider>(),
            new AprsIsFilterBuilderViewModel());
        viewModel.Initialize("N0CALL", 1);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Error: storage unavailable", viewModel.SaveErrorText);
        Assert.Equal(string.Empty, viewModel.BleStatusText);
        nav.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task SaveRetry_ClearsThePreviousError()
    {
        var nav = Substitute.For<INavigationService>();
        var portService = Substitute.For<IPortService>();
        var viewModel = new AddEditPortViewModel(
            nav,
            portService,
            new KissStreamFactory(new List<IKissStreamConnector>()),
            Substitute.For<IBluetoothLeScanner>(),
            Substitute.For<IBluetoothClassicDeviceProvider>(),
            new AprsIsFilterBuilderViewModel());
        viewModel.Initialize("N0CALL", 1);
        viewModel.SaveErrorText = "Error: storage unavailable";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, viewModel.SaveErrorText);
        nav.Received(1).GoBack();
    }

    [Fact]
    public void PopulateFrom_WhenStoredCredentialsAreUnavailable_ShowsTheMarkerAndClearsThePasscode()
    {
        var viewModel = CreateViewModel();

        viewModel.PopulateFrom(UnavailableCredentialsPort());

        Assert.True(viewModel.HasUnavailableCredentials);
        Assert.Equal(string.Empty, viewModel.Passcode);
        Assert.Equal(AprsIsSettings.DefaultServer, viewModel.Server);
    }

    [Fact]
    public void PopulateFrom_WhenStoredCredentialsAreReadable_HidesTheMarker()
    {
        var viewModel = CreateViewModel();
        var config = UnavailableCredentialsPort();
        config.CredentialsUnavailable = false;
        ((AprsIsSettings)config.TypeSettings!).Passcode = "12345";

        viewModel.PopulateFrom(config);

        Assert.False(viewModel.HasUnavailableCredentials);
        Assert.Equal("12345", viewModel.Passcode);
    }

    [Fact]
    public void BuildConfig_WhenThePasscodeWasNotReentered_KeepsThePortReceiveOnly()
    {
        var viewModel = CreateViewModel();
        viewModel.PopulateFrom(UnavailableCredentialsPort());

        var config = viewModel.BuildConfig();

        // The lost secret is already gone; persisting an empty passcode would look
        // configured while every login is rejected.
        Assert.True(config.CredentialsUnavailable);
        Assert.Equal(AprsIsSettings.DefaultPasscode, Assert.IsType<AprsIsSettings>(config.TypeSettings).Passcode);
    }

    [Fact]
    public void BuildConfig_WhenThePasscodeIsReentered_MarksThePortReadableAgain()
    {
        var viewModel = CreateViewModel();
        viewModel.PopulateFrom(UnavailableCredentialsPort());
        viewModel.Passcode = "23456";

        var config = viewModel.BuildConfig();

        Assert.False(config.CredentialsUnavailable);
        Assert.Equal("23456", Assert.IsType<AprsIsSettings>(config.TypeSettings).Passcode);
    }

    private static PortConfig UnavailableCredentialsPort() => new()
    {
        Name = "Test Port",
        TypeSettings = new AprsIsSettings
        {
            Server = AprsIsSettings.DefaultServer,
            ServerPort = AprsIsSettings.DefaultServerPort,
            Passcode = AprsIsSettings.DefaultPasscode,
            Filter = string.Empty
        },
        CredentialsUnavailable = true
    };

    private static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail($"Timed out waiting for {description}.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
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

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Localization;
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
        IBluetoothLeScanner scanner = Substitute.For<IBluetoothLeScanner>();
        scanner.EnsurePermissionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        scanner.ScanAsync(Arg.Any<CancellationToken>())
            .Returns(_ => RepeatAdvertisementAsync(_.Arg<CancellationToken>()));

        AddEditPortViewModel viewModel = CreateViewModel(scanner);
        Task scanTask = viewModel.StartBleScanCommand.ExecuteAsync(null);

        await WaitUntilAsync(
            () => viewModel.BleDevices.Count >= 1,
            "the BLE advertisement to arrive");

        BluetoothLeAdvertisement selected = viewModel.BleDevices[0];
        viewModel.SelectedBleDevice = selected;

        await WaitUntilAsync(
            () => viewModel.IsBleScanning,
            "BLE scanning to start");

        BluetoothLeAdvertisement item = Assert.Single(viewModel.BleDevices);
        Assert.Same(selected, item);
        Assert.Same(selected, viewModel.SelectedBleDevice);

        viewModel.StopBleScanCommand.Execute(null);
        await scanTask;
    }

    [Fact]
    public void BuildConfigUsesLoRaAprsTrackerBleProfile()
    {
        AddEditPortViewModel viewModel = CreateViewModel();
        viewModel.SelectedPortSettingsType = typeof(KissSettings);
        viewModel.SelectedKissTransportType = typeof(BluetoothLeKissTransportSettings);

        BluetoothLeAdvertisement device = new(
            "AA:BB:CC:DD:EE:FF",
            "LilyGO T-Echo Plus",
            -45,
            [BluetoothLeKissTransportSettings.LoRaAprsTrackerServiceUuid]);
        viewModel.BleDevices.Add(device);
        viewModel.SelectedBleDevice = device;

        KissSettings settings = Assert.IsType<KissSettings>(viewModel.BuildConfig().TypeSettings);
        BluetoothLeKissTransportSettings ble = Assert.IsType<BluetoothLeKissTransportSettings>(settings.Transport);

        Assert.Equal(BluetoothLeKissTransportSettings.LoRaAprsTrackerServiceUuid, ble.ServiceUuid);
        Assert.Equal(BluetoothLeKissTransportSettings.LoRaAprsTrackerRxCharacteristicUuid, ble.RxCharacteristicUuid);
        Assert.Equal(BluetoothLeKissTransportSettings.LoRaAprsTrackerTxCharacteristicUuid, ble.TxCharacteristicUuid);
    }

    [Fact]
    public void DefaultsEnableTransmit()
    {
        AddEditPortViewModel viewModel = CreateViewModel();
        Assert.True(viewModel.IsTx);

        PortConfig config = viewModel.BuildConfig();
        Assert.True(config.IsTx);
    }

    [Fact]
    public void PopulateFromAprsIsConfig()
    {
        AddEditPortViewModel viewModel = CreateViewModel();
        PortConfig config = new()
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
        AddEditPortViewModel viewModel = CreateViewModel();
        PortConfig config = new()
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
        AddEditPortViewModel viewModel = CreateViewModel(initialize: false);

        viewModel.Initialize("N0CALL", 1);

        Assert.False(viewModel.IsEditing);
        Assert.Equal("APRS-IS Port 1", viewModel.Name);
        Assert.Equal("Add Port", viewModel.Title);
    }

    [Fact]
    public void InitializeWithExistingConfigUsesEditTitle()
    {
        AddEditPortViewModel viewModel = CreateViewModel(initialize: false);
        PortConfig existing = new()
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
        INavigationService nav = Substitute.For<INavigationService>();
        IPortService portService = Substitute.For<IPortService>();
        portService
            .When(service => service.AddPortAsync(Arg.Any<PortConfig>()))
            .Do(_ => throw new InvalidOperationException("storage unavailable"));
        AddEditPortViewModel viewModel = new(
            nav,
            portService,
            new KissStreamFactory([]),
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
        INavigationService nav = Substitute.For<INavigationService>();
        IPortService portService = Substitute.For<IPortService>();
        AddEditPortViewModel viewModel = new(
            nav,
            portService,
            new KissStreamFactory([]),
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
        AddEditPortViewModel viewModel = CreateViewModel();

        viewModel.PopulateFrom(UnavailableCredentialsPort());

        Assert.True(viewModel.HasUnavailableCredentials);
        Assert.Equal(string.Empty, viewModel.Passcode);
        Assert.Equal(AprsIsSettings.DefaultServer, viewModel.Server);
    }

    [Fact]
    public void PopulateFrom_WhenStoredCredentialsAreReadable_HidesTheMarker()
    {
        AddEditPortViewModel viewModel = CreateViewModel();
        PortConfig config = UnavailableCredentialsPort();
        config.CredentialsUnavailable = false;
        ((AprsIsSettings)config.TypeSettings!).Passcode = "12345";

        viewModel.PopulateFrom(config);

        Assert.False(viewModel.HasUnavailableCredentials);
        Assert.Equal("12345", viewModel.Passcode);
    }

    [Fact]
    public void BuildConfig_WhenThePasscodeWasNotReentered_KeepsThePortReceiveOnly()
    {
        AddEditPortViewModel viewModel = CreateViewModel();
        viewModel.PopulateFrom(UnavailableCredentialsPort());

        PortConfig config = viewModel.BuildConfig();

        // The lost secret is already gone; persisting an empty passcode would look
        // configured while every login is rejected.
        Assert.True(config.CredentialsUnavailable);
        Assert.Equal(AprsIsSettings.DefaultPasscode, Assert.IsType<AprsIsSettings>(config.TypeSettings).Passcode);
    }

    [Fact]
    public void BuildConfig_WhenThePasscodeIsReentered_MarksThePortReadableAgain()
    {
        AddEditPortViewModel viewModel = CreateViewModel();
        viewModel.PopulateFrom(UnavailableCredentialsPort());
        viewModel.Passcode = "23456";

        PortConfig config = viewModel.BuildConfig();

        Assert.False(config.CredentialsUnavailable);
        Assert.Equal("23456", Assert.IsType<AprsIsSettings>(config.TypeSettings).Passcode);
    }

    [Fact]
    public async Task Save_WithBlankServer_ReportsTheFieldAndKeepsTheStoredPort()
    {
        PortEditingHarness harness = CreateHarness(ExistingAprsIsPort());
        harness.ViewModel.Server = " ";

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Get("ValidationServerRequired"), harness.ViewModel.SaveErrorText);
        await harness.PortService.DidNotReceive().UpdatePortAsync(Arg.Any<PortConfig>());
        harness.Navigation.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task Save_WithOutOfRangeServerPort_ReportsTheField()
    {
        PortEditingHarness harness = CreateHarness(ExistingAprsIsPort());
        harness.ViewModel.ServerPort = 70000;

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Format("ValidationPortRange", 1, 65535), harness.ViewModel.SaveErrorText);
        await harness.PortService.DidNotReceive().UpdatePortAsync(Arg.Any<PortConfig>());
    }

    [Fact]
    public async Task Save_WithServerLongerThanTheStoredColumn_ReportsTheField()
    {
        PortEditingHarness harness = CreateHarness(ExistingAprsIsPort());
        harness.ViewModel.Server = new string('a', AprsIsSettings.MaxServerLength + 1);

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(
            Strings.Format("ValidationServerTooLong", AprsIsSettings.MaxServerLength),
            harness.ViewModel.SaveErrorText);
        await harness.PortService.DidNotReceive().UpdatePortAsync(Arg.Any<PortConfig>());
    }

    [Fact]
    public async Task Save_WithBlankPasscode_ReportsTheField()
    {
        PortEditingHarness harness = CreateHarness(ExistingAprsIsPort());
        harness.ViewModel.Passcode = string.Empty;

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Get("ValidationPasscodeRequired"), harness.ViewModel.SaveErrorText);
        await harness.PortService.DidNotReceive().UpdatePortAsync(Arg.Any<PortConfig>());
    }

    [Fact]
    public async Task Save_WithBlankName_ReportsTheField()
    {
        PortEditingHarness harness = CreateHarness(ExistingAprsIsPort());
        harness.ViewModel.Name = "   ";

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Get("ValidationPortNameRequired"), harness.ViewModel.SaveErrorText);
        await harness.PortService.DidNotReceive().UpdatePortAsync(Arg.Any<PortConfig>());
    }

    [Fact]
    public async Task Save_WithBluetoothTransportAndNoDevice_ReportsTheField()
    {
        PortEditingHarness harness = CreateHarness();
        harness.ViewModel.SelectedPortSettingsType = typeof(KissSettings);
        harness.ViewModel.SelectedKissTransportType = typeof(BluetoothLeKissTransportSettings);

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Get("ValidationBluetoothDeviceRequired"), harness.ViewModel.SaveErrorText);
        await harness.PortService.DidNotReceive().AddPortAsync(Arg.Any<PortConfig>());
    }

    [Fact]
    public async Task Save_WhenThePasscodeIsStillUnavailable_PersistsAReceiveOnlyPort()
    {
        PortEditingHarness harness = CreateHarness(UnavailableCredentialsPort());

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, harness.ViewModel.SaveErrorText);
        await harness.PortService.Received(1).UpdatePortAsync(Arg.Is<PortConfig>(config =>
            config.CredentialsUnavailable &&
            config.TypeSettings != null &&
            ((AprsIsSettings)config.TypeSettings).Passcode == AprsIsSettings.DefaultPasscode));
    }

    [Fact]
    public async Task Save_WithValidEdits_PersistsTheEditedValues()
    {
        PortEditingHarness harness = CreateHarness(ExistingAprsIsPort());
        harness.ViewModel.Server = "second.example.org";
        harness.ViewModel.ServerPort = 14581;
        harness.ViewModel.Passcode = "23456";

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, harness.ViewModel.SaveErrorText);
        await harness.PortService.Received(1).UpdatePortAsync(Arg.Is<PortConfig>(config =>
            config.TypeSettings != null &&
            ((AprsIsSettings)config.TypeSettings).Server == "second.example.org" &&
            ((AprsIsSettings)config.TypeSettings).ServerPort == 14581 &&
            ((AprsIsSettings)config.TypeSettings).Passcode == "23456"));
        harness.Navigation.Received(1).GoBack();
    }

    private static PortConfig ExistingAprsIsPort() => new()
    {
        Name = "Test Port",
        TypeSettings = new AprsIsSettings
        {
            Server = AprsIsSettings.DefaultServer,
            ServerPort = AprsIsSettings.DefaultServerPort,
            Passcode = "12345",
            Filter = string.Empty
        }
    };

    private static PortEditingHarness CreateHarness(PortConfig? existingConfig = null)
    {
        INavigationService navigation = Substitute.For<INavigationService>();
        IPortService portService = Substitute.For<IPortService>();
        AddEditPortViewModel viewModel = new(
            navigation,
            portService,
            new KissStreamFactory([]),
            Substitute.For<IBluetoothLeScanner>(),
            Substitute.For<IBluetoothClassicDeviceProvider>(),
            new AprsIsFilterBuilderViewModel());
        viewModel.Initialize("N0CALL", 1, existingConfig);
        return new PortEditingHarness(viewModel, portService, navigation);
    }

    private sealed record PortEditingHarness(
        AddEditPortViewModel ViewModel,
        IPortService PortService,
        INavigationService Navigation);

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
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
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
        BluetoothLeAdvertisement advertisement = new(
            "AA:BB:CC:DD:EE:FF",
            "Test BLE",
            -40,
            []);
        yield return advertisement;
        yield return advertisement with { Rssi = -60 };
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static AddEditPortViewModel CreateViewModel(
        IBluetoothLeScanner? bleScanner = null,
        bool initialize = true)
    {
        INavigationService nav = Substitute.For<INavigationService>();
        IPortService portService = Substitute.For<IPortService>();
        KissStreamFactory kissFactory = new([]);
        bleScanner ??= Substitute.For<IBluetoothLeScanner>();
        IBluetoothClassicDeviceProvider btClassic = Substitute.For<IBluetoothClassicDeviceProvider>();

        AddEditPortViewModel vm = new(nav, portService, kissFactory, bleScanner, btClassic, new AprsIsFilterBuilderViewModel());
        if (initialize)
        {
            vm.Initialize("N0CALL", 1);
        }
        return vm;
    }
}

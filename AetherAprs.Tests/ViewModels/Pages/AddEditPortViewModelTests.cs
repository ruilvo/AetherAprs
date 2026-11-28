// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Localization;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Services.Platform;
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

        // The provider text stays in the log; the operator sees a stable, localized
        // category instead of an English exception message.
        Assert.Equal(
            Strings.Format("ErrorPrefix", Strings.Get("FailureUnavailable")),
            viewModel.SaveErrorText);
        Assert.DoesNotContain("storage unavailable", viewModel.SaveErrorText);
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
        viewModel.SaveErrorText = Strings.Format("ErrorPrefix", Strings.Get("FailureUnavailable"));

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

    [Fact]
    public void PopulateFrom_WhenTheStoredTransportHasNoConnector_KeepsItAndReportsItUnavailable()
    {
        // A port stored on a phone with Bluetooth support, opened where the connector
        // set only has TCP: the transport must be shown as saved and labelled
        // unavailable, not silently rewritten to TCP.
        PortEditingHarness harness = CreateHarness(CreateFactory(typeof(TcpKissTransportSettings)));

        harness.ViewModel.PopulateFrom(StoredBluetoothLePort());

        Assert.Equal(typeof(BluetoothLeKissTransportSettings), harness.ViewModel.SelectedKissTransportType);
        Assert.Contains(typeof(BluetoothLeKissTransportSettings), harness.ViewModel.AvailableKissTransportTypes);
        Assert.Equal(
            Strings.Format("KissTransportUnavailable", Strings.Get("TransportBluetoothLe")),
            harness.ViewModel.UnavailableTransportText);
    }

    [Fact]
    public void PopulateFrom_WhenTheStoredTransportIsSupported_ShowsNoWarning()
    {
        PortEditingHarness harness = CreateHarness(CreateFactory(typeof(TcpKissTransportSettings)));

        harness.ViewModel.PopulateFrom(StoredBluetoothLePort());
        harness.ViewModel.SelectedKissTransportType = typeof(TcpKissTransportSettings);

        Assert.Equal(string.Empty, harness.ViewModel.UnavailableTransportText);
    }

    [Fact]
    public void FallbackTransportOnAHostWithNoConnectors_IsNotReportedUnavailable()
    {
        // The desktop host registers Bluetooth connectors that report themselves as
        // unsupported, which leaves no transport the editor can offer. That says
        // nothing about the only route it can show, so it must not be labelled
        // unavailable.
        PortEditingHarness harness = CreateHarness(
            new KissStreamFactory(
            [
                new UnsupportedBluetoothClassicKissStreamConnector(),
                new UnsupportedBluetoothLeKissStreamConnector()
            ]));

        Assert.Equal(typeof(TcpKissTransportSettings), harness.ViewModel.SelectedKissTransportType);
        Assert.Equal([typeof(TcpKissTransportSettings)], harness.ViewModel.AvailableKissTransportTypes);
        Assert.Equal(string.Empty, harness.ViewModel.UnavailableTransportText);
    }

    [Fact]
    public async Task Save_WithAnUnavailableStoredTransport_KeepsTheStoredTransportVerbatim()
    {
        PortEditingHarness harness = CreateHarness(
            CreateFactory(typeof(TcpKissTransportSettings)),
            StoredBluetoothLePort());
        harness.ViewModel.Name = "Renamed Port";

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, harness.ViewModel.SaveErrorText);
        await harness.PortService.Received(1).UpdatePortAsync(Arg.Is<PortConfig>(config =>
            config.Name == "Renamed Port" &&
            config.TypeSettings != null &&
            ((KissSettings)config.TypeSettings).Transport != null &&
            ((KissSettings)config.TypeSettings).Transport!.GetType() == typeof(BluetoothLeKissTransportSettings) &&
            ((BluetoothLeKissTransportSettings)((KissSettings)config.TypeSettings).Transport!).DeviceAddress == "AA:BB:CC:DD:EE:01"));
    }

    [Fact]
    public async Task Save_WithAnUnavailableStoredTransport_DoesNotWarnAboutAMissingDevice()
    {
        // The device was stored with the port; the missing connector says nothing
        // about the device, so blaming the device would send the operator looking for
        // the wrong problem.
        PortEditingHarness harness = CreateHarness(
            CreateFactory(typeof(TcpKissTransportSettings)),
            StoredBluetoothLePort());

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.NotEqual(Strings.Get("ValidationBluetoothDeviceRequired"), harness.ViewModel.SaveErrorText);
    }

    [Fact]
    public void PopulateFromAfskConfig_RoundTripsEverySetting()
    {
        AddEditPortViewModel viewModel = CreateViewModel(pttService: PhysicalPttService(true));
        PortConfig config = new()
        {
            Name = "Radio",
            TypeSettings = new AfskSettings
            {
                PttMode = PttMode.SpeakerMic,
                TxDelayMs = 250,
                TxTailMs = 40,
                SlotTimeMs = 120,
                Persist = 200
            }
        };

        viewModel.PopulateFrom(config);

        Assert.Equal(typeof(AfskSettings), viewModel.SelectedPortSettingsType);
        Assert.Equal(PttMode.SpeakerMic, viewModel.SelectedPttMode);
        Assert.Equal(250, viewModel.AfskTxDelayMs);
        Assert.Equal(40, viewModel.AfskTxTailMs);
        Assert.Equal(120, viewModel.AfskSlotTimeMs);
        Assert.Equal(200, viewModel.AfskPersist);

        AfskSettings rebuilt = Assert.IsType<AfskSettings>(viewModel.BuildConfig().TypeSettings);
        Assert.Equal(PttMode.SpeakerMic, rebuilt.PttMode);
        Assert.Equal(250, rebuilt.TxDelayMs);
        Assert.Equal(40, rebuilt.TxTailMs);
        Assert.Equal(120, rebuilt.SlotTimeMs);
        Assert.Equal(200, rebuilt.Persist);
    }

    [Fact]
    public void AfskPort_OffersEveryModeWhenThePlatformCanKeyTheTransmitter()
    {
        AddEditPortViewModel viewModel = CreateViewModel(pttService: PhysicalPttService(true));

        Assert.True(viewModel.SupportsPhysicalPtt);
        Assert.Contains(PttMode.Vox, viewModel.AvailablePttModes);
        Assert.Contains(PttMode.SpeakerMic, viewModel.AvailablePttModes);
        Assert.Equal(string.Empty, viewModel.PttModeUnavailableText);
    }

    [Fact]
    public void AfskPort_OffersOnlyVoxWhenThePlatformHasNoPhysicalPtt()
    {
        AddEditPortViewModel viewModel = CreateViewModel(pttService: PhysicalPttService(false));

        Assert.False(viewModel.SupportsPhysicalPtt);
        Assert.Equal([PttMode.Vox], viewModel.AvailablePttModes);
        Assert.Equal(Strings.Get("PttModeUnavailable"), viewModel.PttModeUnavailableText);
    }

    [Fact]
    public void AfskPort_WithoutPttServiceOffersOnlyVox()
    {
        AddEditPortViewModel viewModel = CreateViewModel();

        Assert.False(viewModel.SupportsPhysicalPtt);
        Assert.Equal([PttMode.Vox], viewModel.AvailablePttModes);
    }

    [Fact]
    public void AfskPort_KeepsAnUnsupportedStoredModeSelectableSoItRoundTrips()
    {
        // Stored on a phone with a speaker-mic, opened where only VOX can be keyed:
        // the saved mode must still be shown and saved verbatim.
        AddEditPortViewModel viewModel = CreateViewModel(pttService: PhysicalPttService(false));
        PortConfig config = new()
        {
            Name = "Radio",
            TypeSettings = new AfskSettings { PttMode = PttMode.SpeakerMic }
        };

        viewModel.PopulateFrom(config);

        Assert.Equal(PttMode.SpeakerMic, viewModel.SelectedPttMode);
        Assert.Contains(PttMode.SpeakerMic, viewModel.AvailablePttModes);
        Assert.Equal(PttMode.SpeakerMic, Assert.IsType<AfskSettings>(viewModel.BuildConfig().TypeSettings).PttMode);
    }

    [Fact]
    public async Task Save_WithAfskPortPersistsTheEditedSettings()
    {
        PortEditingHarness harness = CreateHarness(pttService: PhysicalPttService(true));
        harness.ViewModel.SelectedPortSettingsType = typeof(AfskSettings);
        harness.ViewModel.SelectedPttMode = PttMode.SpeakerMic;
        harness.ViewModel.AfskTxDelayMs = 150;
        harness.ViewModel.AfskTxTailMs = 30;
        harness.ViewModel.AfskSlotTimeMs = 90;
        harness.ViewModel.AfskPersist = 128;

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, harness.ViewModel.SaveErrorText);
        await harness.PortService.Received(1).AddPortAsync(Arg.Is<PortConfig>(config =>
            config.TypeSettings!.GetType() == typeof(AfskSettings) &&
            ((AfskSettings)config.TypeSettings).PttMode == PttMode.SpeakerMic &&
            ((AfskSettings)config.TypeSettings).TxDelayMs == 150 &&
            ((AfskSettings)config.TypeSettings).TxTailMs == 30 &&
            ((AfskSettings)config.TypeSettings).SlotTimeMs == 90 &&
            ((AfskSettings)config.TypeSettings).Persist == 128));
        harness.Navigation.Received(1).GoBack();
    }

    [Theory]
    [InlineData("delay", -1)]
    [InlineData("tail", -1)]
    [InlineData("slot", -1)]
    public async Task Save_WithNegativeAfskTiming_ReportsTheField(string field, int value)
    {
        PortEditingHarness harness = CreateHarness(pttService: PhysicalPttService(true));
        harness.ViewModel.SelectedPortSettingsType = typeof(AfskSettings);
        switch (field)
        {
            case "delay":
                harness.ViewModel.AfskTxDelayMs = value;
                break;
            case "tail":
                harness.ViewModel.AfskTxTailMs = value;
                break;
            default:
                harness.ViewModel.AfskSlotTimeMs = value;
                break;
        }

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        string expected = field switch
        {
            "delay" => Strings.Get("ValidationTxDelayRange"),
            "tail" => Strings.Get("ValidationTxTailRange"),
            _ => Strings.Get("ValidationSlotTimeRange")
        };
        Assert.Equal(expected, harness.ViewModel.SaveErrorText);
        await harness.PortService.DidNotReceive().AddPortAsync(Arg.Any<PortConfig>());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public async Task Save_WithOutOfRangeAfskPersist_ReportsTheField(int persist)
    {
        PortEditingHarness harness = CreateHarness(pttService: PhysicalPttService(true));
        harness.ViewModel.SelectedPortSettingsType = typeof(AfskSettings);
        harness.ViewModel.AfskPersist = persist;

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Get("ValidationPersistRange"), harness.ViewModel.SaveErrorText);
        await harness.PortService.DidNotReceive().AddPortAsync(Arg.Any<PortConfig>());
    }

    private static IPttService PhysicalPttService(bool supportsPhysicalPtt)
    {
        IPttService service = Substitute.For<IPttService>();
        service.SupportsPhysicalPtt.Returns(supportsPhysicalPtt);
        return service;
    }

    private static KissStreamFactory CreateFactory(params Type[] supportedSettingsTypes) =>
        new(supportedSettingsTypes.Select(settingsType => (IKissStreamConnector)new SupportedStubConnector(settingsType)));

    private static PortConfig StoredBluetoothLePort()
    {
        PortConfig config = StoredBluetoothLeConfig();
        config.Id = Guid.NewGuid();
        return config;
    }

    private static PortConfig StoredBluetoothLeConfig() => new()
    {
        Name = "Tracker",
        TypeSettings = new KissSettings
        {
            Transport = new BluetoothLeKissTransportSettings
            {
                DeviceAddress = "AA:BB:CC:DD:EE:01",
                DeviceName = "T-Echo"
            }
        }
    };

    private sealed class SupportedStubConnector(Type settingsType) : IKissStreamConnector
    {
        public Type SettingsType { get; } = settingsType;

        public bool IsSupported => true;

        public Task<System.IO.Stream> ConnectAsync(
            IKissTransportSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<System.IO.Stream>(new System.IO.MemoryStream());
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
        => CreateHarness(new KissStreamFactory([]), existingConfig, null);

    private static PortEditingHarness CreateHarness(IPttService pttService, PortConfig? existingConfig = null)
        => CreateHarness(new KissStreamFactory([]), existingConfig, pttService);

    private static PortEditingHarness CreateHarness(
        IKissStreamFactory kissStreamFactory,
        PortConfig? existingConfig = null,
        IPttService? pttService = null)
    {
        INavigationService navigation = Substitute.For<INavigationService>();
        IPortService portService = Substitute.For<IPortService>();
        AddEditPortViewModel viewModel = new(
            navigation,
            portService,
            kissStreamFactory,
            Substitute.For<IBluetoothLeScanner>(),
            Substitute.For<IBluetoothClassicDeviceProvider>(),
            new AprsIsFilterBuilderViewModel(),
            pttService: pttService);
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
        bool initialize = true,
        IPttService? pttService = null)
    {
        INavigationService nav = Substitute.For<INavigationService>();
        IPortService portService = Substitute.For<IPortService>();
        KissStreamFactory kissFactory = new([]);
        bleScanner ??= Substitute.For<IBluetoothLeScanner>();
        IBluetoothClassicDeviceProvider btClassic = Substitute.For<IBluetoothClassicDeviceProvider>();

        AddEditPortViewModel vm = new(nav, portService, kissFactory, bleScanner, btClassic, new AprsIsFilterBuilderViewModel(), pttService: pttService);
        if (initialize)
        {
            vm.Initialize("N0CALL", 1);
        }
        return vm;
    }
}

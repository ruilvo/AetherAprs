// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Helpers;
using AetherAprs.Localization;
using AetherAprs.Services;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Transports.Kiss;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

public partial class AddEditPortViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IPortService _portService;
    private readonly IBluetoothLeScanner _bleScanner;
    private readonly IBluetoothClassicDeviceProvider _classicDeviceProvider;
    private CancellationTokenSource? _bleScanCts;
    private PortConfig? _existingConfig;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial Type SelectedPortSettingsType { get; set; } = typeof(AprsIsSettings);

    [ObservableProperty]
    public partial string Server { get; set; } = AprsIsSettings.DefaultServer;

    [ObservableProperty]
    public partial int ServerPort { get; set; } = AprsIsSettings.DefaultServerPort;

    [ObservableProperty]
    public partial string Passcode { get; set; } = AprsIsSettings.DefaultPasscode;

    [ObservableProperty]
    public partial string Filter { get; set; } = AprsIsSettings.DefaultFilter;

    [ObservableProperty]
    public partial Type SelectedKissTransportType { get; set; } = typeof(TcpKissTransportSettings);

    [ObservableProperty]
    public partial string TcpHost { get; set; } = TcpKissTransportSettings.DefaultHost;

    [ObservableProperty]
    public partial int TcpPort { get; set; } = TcpKissTransportSettings.DefaultPort;

    [ObservableProperty]
    public partial BluetoothLeAdvertisement? SelectedBleDevice { get; set; }

    [ObservableProperty]
    public partial BluetoothClassicDevice? SelectedSppDevice { get; set; }

    [ObservableProperty]
    public partial bool IsBleScanning { get; set; }

    [ObservableProperty]
    public partial string BleStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SppStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRx { get; set; } = true;

    [ObservableProperty]
    public partial bool IsTx { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowOnMap { get; set; } = true;

    public ObservableCollection<BluetoothLeAdvertisement> BleDevices { get; } = new();

    public ObservableCollection<BluetoothClassicDevice> SppDevices { get; } = new();

    public bool IsEditing => _existingConfig is not null;

    public string Title => IsEditing
        ? (string.IsNullOrEmpty(Name) ? Strings.Get("EditPort") : Strings.Format("EditPortNamed", Name))
        : Strings.Get("AddPort");

    public Type[] PortTypes { get; } = [typeof(AprsIsSettings), typeof(KissSettings)];

    public IReadOnlyList<Type> AvailableKissTransportTypes { get; }

    public AddEditPortViewModel(
        INavigationService navigationService,
        IPortService portService,
        IKissStreamFactory kissStreamFactory,
        IBluetoothLeScanner bleScanner,
        IBluetoothClassicDeviceProvider classicDeviceProvider)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _portService = portService ?? throw new ArgumentNullException(nameof(portService));
        ArgumentNullException.ThrowIfNull(kissStreamFactory);
        _bleScanner = bleScanner ?? throw new ArgumentNullException(nameof(bleScanner));
        _classicDeviceProvider = classicDeviceProvider ?? throw new ArgumentNullException(nameof(classicDeviceProvider));

        var supported = kissStreamFactory.SupportedTransports;
        AvailableKissTransportTypes = supported.Count > 0
            ? supported.ToArray()
            : [typeof(TcpKissTransportSettings)];
        SelectedKissTransportType = AvailableKissTransportTypes.Contains(typeof(TcpKissTransportSettings))
            ? typeof(TcpKissTransportSettings)
            : AvailableKissTransportTypes[0];

        Name = string.Empty;
        Passcode = AprsIsSettings.DefaultPasscode;
    }

    public void Initialize(string globalCallsign, int nextPortNumber, PortConfig? existingConfig = null)
    {
        _existingConfig = existingConfig;
        OnPropertyChanged(nameof(IsEditing));

        if (existingConfig != null)
        {
            PopulateFrom(existingConfig);
        }
        else
        {
            Name = Strings.Format("DefaultAprsIsPortName", nextPortNumber);
            Passcode = AprsPasscode.Compute(globalCallsign);
        }

        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand]
    private void Cancel()
    {
        StopBleScan();
        _navigationService.GoBack();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        StopBleScan();

        var config = BuildConfig();

        if (_existingConfig != null)
        {
            config.Id = _existingConfig.Id;
            config.IsEnabled = _existingConfig.IsEnabled;
            await _portService.UpdatePortAsync(config);
        }
        else
        {
            await _portService.AddPortAsync(config);
        }

        _navigationService.GoBack();
    }

    [RelayCommand]
    private async Task StartBleScanAsync()
    {
        if (IsBleScanning)
        {
            return;
        }

        BleStatusText = string.Empty;
        BleDevices.Clear();
        SelectedBleDevice = null;

        try
        {
            await _bleScanner.EnsurePermissionAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            BleStatusText = ex.Message;
            return;
        }

        _bleScanCts?.Cancel();
        _bleScanCts?.Dispose();
        _bleScanCts = new CancellationTokenSource();
        var token = _bleScanCts.Token;
        IsBleScanning = true;

        try
        {
            await foreach (var advertisement in _bleScanner.ScanAsync(token).ConfigureAwait(true))
            {
                var existing = BleDevices.FirstOrDefault(device =>
                    string.Equals(device.Address, advertisement.Address, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    var index = BleDevices.IndexOf(existing);
                    BleDevices[index] = advertisement;
                    if (ReferenceEquals(SelectedBleDevice, existing))
                    {
                        SelectedBleDevice = advertisement;
                    }
                }
                else
                {
                    BleDevices.Add(advertisement);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the user stops scanning or navigates away.
        }
        catch (Exception ex)
        {
            BleStatusText = ex.Message;
        }
        finally
        {
            IsBleScanning = false;
        }
    }

    [RelayCommand]
    private void StopBleScan()
    {
        _bleScanCts?.Cancel();
        _bleScanCts?.Dispose();
        _bleScanCts = null;
        IsBleScanning = false;
    }

    [RelayCommand]
    private async Task RefreshSppDevicesAsync()
    {
        SppStatusText = string.Empty;

        try
        {
            await _classicDeviceProvider.EnsurePermissionAsync().ConfigureAwait(true);
            var devices = await _classicDeviceProvider.GetBondedDevicesAsync().ConfigureAwait(true);
            var selectedAddress = SelectedSppDevice?.Address;

            SppDevices.Clear();
            foreach (var device in devices)
            {
                SppDevices.Add(device);
            }

            SelectedSppDevice = selectedAddress is null
                ? null
                : SppDevices.FirstOrDefault(device =>
                    string.Equals(device.Address, selectedAddress, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            SppStatusText = ex.Message;
        }
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(Title));
    }

    partial void OnSelectedKissTransportTypeChanged(Type value)
    {
        if (value != typeof(BluetoothLeKissTransportSettings))
        {
            StopBleScan();
        }
    }

    partial void OnSelectedPortSettingsTypeChanged(Type value)
    {
        if (value != typeof(KissSettings))
        {
            StopBleScan();
        }
    }

    internal void PopulateFrom(PortConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Name = config.Name;
        SelectedPortSettingsType = config.TypeSettings switch
        {
            KissSettings => typeof(KissSettings),
            _ => typeof(AprsIsSettings)
        };
        IsRx = config.IsRx;
        IsTx = config.IsTx;
        ShowOnMap = config.ShowOnMap;

        if (config.TypeSettings is AprsIsSettings aprsIs)
        {
            Server = aprsIs.Server;
            ServerPort = aprsIs.ServerPort;
            Passcode = aprsIs.Passcode;
            Filter = aprsIs.Filter;
        }

        if (config.TypeSettings is KissSettings kiss)
        {
            var transportType = kiss.Transport?.GetType();
            if (transportType is not null && AvailableKissTransportTypes.Contains(transportType))
            {
                SelectedKissTransportType = transportType;
            }

            switch (kiss.Transport)
            {
                case TcpKissTransportSettings tcp:
                    TcpHost = tcp.Host;
                    TcpPort = tcp.Port;
                    break;

                case BluetoothLeKissTransportSettings ble:
                    var bleDevice = new BluetoothLeAdvertisement(
                        ble.DeviceAddress,
                        ble.DeviceName,
                        0,
                        []);
                    BleDevices.Clear();
                    if (!string.IsNullOrWhiteSpace(ble.DeviceAddress))
                    {
                        BleDevices.Add(bleDevice);
                        SelectedBleDevice = bleDevice;
                    }
                    break;

                case BluetoothClassicKissTransportSettings spp:
                    var sppDevice = new BluetoothClassicDevice(spp.DeviceAddress, spp.DeviceName);
                    SppDevices.Clear();
                    if (!string.IsNullOrWhiteSpace(spp.DeviceAddress))
                    {
                        SppDevices.Add(sppDevice);
                        SelectedSppDevice = sppDevice;
                    }
                    break;
            }
        }
    }

    internal PortConfig BuildConfig()
    {
        var config = new PortConfig
        {
            Name = Name,
            IsRx = IsRx,
            IsTx = IsTx,
            ShowOnMap = ShowOnMap
        };

        if (SelectedPortSettingsType == typeof(AprsIsSettings))
        {
            config.TypeSettings = new AprsIsSettings
            {
                Server = Server,
                ServerPort = ServerPort,
                Passcode = Passcode,
                Filter = Filter
            };
        }
        else if (SelectedPortSettingsType == typeof(KissSettings))
        {
            config.TypeSettings = new KissSettings
            {
                Transport = BuildKissTransportSettings()
            };
        }

        return config;
    }

    private IKissTransportSettings BuildKissTransportSettings()
    {
        if (SelectedKissTransportType == typeof(TcpKissTransportSettings))
        {
            return new TcpKissTransportSettings
            {
                Host = string.IsNullOrWhiteSpace(TcpHost) ? TcpKissTransportSettings.DefaultHost : TcpHost,
                Port = TcpPort
            };
        }

        if (SelectedKissTransportType == typeof(BluetoothLeKissTransportSettings))
        {
            return new BluetoothLeKissTransportSettings
            {
                DeviceAddress = SelectedBleDevice?.Address ?? string.Empty,
                DeviceName = SelectedBleDevice?.Name
            };
        }

        if (SelectedKissTransportType == typeof(BluetoothClassicKissTransportSettings))
        {
            return new BluetoothClassicKissTransportSettings
            {
                DeviceAddress = SelectedSppDevice?.Address ?? string.Empty,
                DeviceName = SelectedSppDevice?.Name
            };
        }

        throw new InvalidOperationException($"Unsupported KISS transport type '{SelectedKissTransportType.Name}'.");
    }

}

public sealed record AprsSymbolOption(
    string Name,
    string TableCharacter,
    string CodeCharacter);

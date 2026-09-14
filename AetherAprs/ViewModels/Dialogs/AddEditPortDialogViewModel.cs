// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AetherAprs.Configuration;
using AetherAprs.Helpers;
using AetherAprs.Imaging;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Transports.Kiss;
using Avalonia.Media.Imaging;
using DialogHostAvalonia;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels;

public partial class AddEditPortDialogViewModel : ViewModelBase
{
    private readonly IAprsSymbolBitmapProvider _symbolBitmapProvider;
    private readonly Func<SKBitmap, Bitmap?> _previewFactory;
    private readonly IBluetoothLeScanner _bleScanner;
    private readonly IBluetoothClassicDeviceProvider _classicDeviceProvider;
    private CancellationTokenSource? _bleScanCts;

    public IReadOnlyList<AprsSymbolOption> SymbolOptions { get; } =
    [
        new("Human", "/", "["),
        new("Ambulance", "/", "a"),
        new("Car", "/", ">"),
        new("Truck", "/", "k"),
        new("Boat", "/", "s"),
        new("Emergency", "\\", "!"),
        new("Hospital", "\\", "h")
    ];

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial PortType SelectedPortType { get; set; } = PortType.AprsIs;

    [ObservableProperty]
    public partial string Server { get; set; } = AprsIsSettings.DefaultServer;

    [ObservableProperty]
    public partial int ServerPort { get; set; } = AprsIsSettings.DefaultServerPort;

    [ObservableProperty]
    public partial string Passcode { get; set; } = AprsIsSettings.DefaultPasscode;

    [ObservableProperty]
    public partial string Filter { get; set; } = AprsIsSettings.DefaultFilter;

    [ObservableProperty]
    public partial int? Ssid { get; set; }

    [ObservableProperty]
    public partial KissTransportKind SelectedKissTransportKind { get; set; } = KissTransportKind.Tcp;

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
    public partial string SymbolTableCharacter { get; set; } = "/";

    [ObservableProperty]
    public partial string SymbolCodeCharacter { get; set; } = "[";

    [ObservableProperty]
    public partial bool UseDefaultSymbol { get; set; } = true;

    [ObservableProperty]
    public partial AprsSymbolOption? SelectedSymbolOption { get; set; }

    [ObservableProperty]
    public partial Bitmap? SymbolPreview { get; private set; }

    [ObservableProperty]
    public partial bool IsSymbolValid { get; private set; }

    [ObservableProperty]
    public partial bool IsRx { get; set; } = true;

    [ObservableProperty]
    public partial bool IsTx { get; set; } = false;

    [ObservableProperty]
    public partial DynamicBeaconMode? SelectedBeaconMode { get; set; }

    [ObservableProperty]
    public partial bool UseDefaultBeaconMode { get; set; } = true;

    public ObservableCollection<BluetoothLeAdvertisement> BleDevices { get; } = new();

    public ObservableCollection<BluetoothClassicDevice> SppDevices { get; } = new();

    public string Title => string.IsNullOrEmpty(Name) ? "Add Port" : $"Edit {Name}";

    public PortType[] PortTypes { get; } = [PortType.AprsIs, PortType.Kiss];

    public IReadOnlyList<KissTransportKind> AvailableKissTransportKinds { get; }

    public DynamicBeaconMode?[] BeaconModes { get; } = [null, DynamicBeaconMode.Walk, DynamicBeaconMode.Drive, DynamicBeaconMode.Custom];

    public AddEditPortDialogViewModel(
        string globalCallsign,
        int nextPortNumber,
        IAprsSymbolBitmapProvider symbolBitmapProvider,
        IKissStreamFactory kissStreamFactory,
        IBluetoothLeScanner bleScanner,
        IBluetoothClassicDeviceProvider classicDeviceProvider,
        Func<SKBitmap, Bitmap?>? previewFactory = null,
        string defaultSymbolTableCharacter = "/",
        string defaultSymbolCodeCharacter = "[",
        DynamicBeaconMode defaultBeaconMode = DynamicBeaconMode.Walk)
    {
        ArgumentNullException.ThrowIfNull(kissStreamFactory);
        _symbolBitmapProvider = symbolBitmapProvider ?? throw new ArgumentNullException(nameof(symbolBitmapProvider));
        _bleScanner = bleScanner ?? throw new ArgumentNullException(nameof(bleScanner));
        _classicDeviceProvider = classicDeviceProvider ?? throw new ArgumentNullException(nameof(classicDeviceProvider));
        _previewFactory = previewFactory ?? CreatePreviewBitmap;

        var supported = kissStreamFactory.SupportedTransports;
        AvailableKissTransportKinds = supported.Count > 0
            ? supported.ToArray()
            : [KissTransportKind.Tcp];
        SelectedKissTransportKind = AvailableKissTransportKinds.Contains(KissTransportKind.Tcp)
            ? KissTransportKind.Tcp
            : AvailableKissTransportKinds[0];

        Name = $"APRS-IS Port {nextPortNumber}";
        Passcode = AprsPasscode.Compute(globalCallsign);
        SymbolTableCharacter = defaultSymbolTableCharacter;
        SymbolCodeCharacter = defaultSymbolCodeCharacter;
        SelectedBeaconMode = defaultBeaconMode;
        UpdateSelectedSymbolOption();
        UpdateSymbolPreview();
    }

    [RelayCommand]
    private void Cancel()
    {
        StopBleScan();
        DialogHost.Close("MainDialogHost", "CANCEL");
    }

    [RelayCommand(CanExecute = nameof(IsSymbolValid))]
    private void Save()
    {
        StopBleScan();
        DialogHost.Close("MainDialogHost", "OK");
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
            // Expected when the user stops scanning or the dialog closes.
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

    partial void OnSymbolTableCharacterChanged(string value)
    {
        UpdateSelectedSymbolOption();
        UpdateSymbolPreview();
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSymbolCodeCharacterChanged(string value)
    {
        UpdateSelectedSymbolOption();
        UpdateSymbolPreview();
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSymbolOptionChanged(AprsSymbolOption? value)
    {
        if (value is null)
        {
            return;
        }

        SymbolTableCharacter = value.TableCharacter;
        SymbolCodeCharacter = value.CodeCharacter;
    }

    partial void OnSelectedKissTransportKindChanged(KissTransportKind value)
    {
        if (value != KissTransportKind.BluetoothLe)
        {
            StopBleScan();
        }
    }

    partial void OnSelectedPortTypeChanged(PortType value)
    {
        if (value != PortType.Kiss)
        {
            StopBleScan();
        }
    }

    [RelayCommand]
    private void SelectSymbol(AprsSymbolOption option)
    {
        SelectedSymbolOption = option;
    }

    public void PopulateFrom(PortItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        PopulateFrom(item.BuildConfig());
    }

    public void PopulateFrom(PortConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Name = config.Name;
        SelectedPortType = config.Type;
        IsRx = config.IsRx;
        IsTx = config.IsTx;
        Ssid = config.Ssid;

        UseDefaultSymbol = config.SymbolTableCharacter is null || config.SymbolCodeCharacter is null;
        if (!UseDefaultSymbol)
        {
            SymbolTableCharacter = config.SymbolTableCharacter!;
            SymbolCodeCharacter = config.SymbolCodeCharacter!;
        }

        UseDefaultBeaconMode = config.DynamicBeaconMode is null;
        if (!UseDefaultBeaconMode)
        {
            SelectedBeaconMode = config.DynamicBeaconMode;
        }

        if (config.TypeSettings is AprsIsSettings aprsIs)
        {
            Server = aprsIs.Server;
            ServerPort = aprsIs.ServerPort;
            Passcode = aprsIs.Passcode;
            Filter = aprsIs.Filter;
        }

        if (config.TypeSettings is KissSettings kiss)
        {
            if (AvailableKissTransportKinds.Contains(kiss.TransportKind))
            {
                SelectedKissTransportKind = kiss.TransportKind;
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
                        Array.Empty<Guid>());
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

    public PortConfig BuildConfig()
    {
        if (!TryCreateSymbol(out _))
        {
            throw new InvalidOperationException("The APRS symbol table and code must each contain one valid character.");
        }

        var config = new PortConfig
        {
            Type = SelectedPortType,
            Name = Name,
            Ssid = Ssid,
            SymbolTableCharacter = UseDefaultSymbol ? null : SymbolTableCharacter,
            SymbolCodeCharacter = UseDefaultSymbol ? null : SymbolCodeCharacter,
            IsRx = IsRx,
            IsTx = IsTx,
            DynamicBeaconMode = UseDefaultBeaconMode ? null : SelectedBeaconMode
        };

        if (SelectedPortType == PortType.AprsIs)
        {
            config.TypeSettings = new AprsIsSettings
            {
                Server = Server,
                ServerPort = ServerPort,
                Passcode = Passcode,
                Filter = Filter
            };
        }
        else if (SelectedPortType == PortType.Kiss)
        {
            config.TypeSettings = new KissSettings
            {
                TransportKind = SelectedKissTransportKind,
                Transport = BuildKissTransportSettings()
            };
        }

        return config;
    }

    private IKissTransportSettings BuildKissTransportSettings()
    {
        return SelectedKissTransportKind switch
        {
            KissTransportKind.Tcp => new TcpKissTransportSettings
            {
                Host = string.IsNullOrWhiteSpace(TcpHost) ? TcpKissTransportSettings.DefaultHost : TcpHost,
                Port = TcpPort
            },
            KissTransportKind.BluetoothLe => new BluetoothLeKissTransportSettings
            {
                DeviceAddress = SelectedBleDevice?.Address ?? string.Empty,
                DeviceName = SelectedBleDevice?.Name
            },
            KissTransportKind.BluetoothClassic => new BluetoothClassicKissTransportSettings
            {
                DeviceAddress = SelectedSppDevice?.Address ?? string.Empty,
                DeviceName = SelectedSppDevice?.Name
            },
            _ => throw new InvalidOperationException($"Unsupported KISS transport kind '{SelectedKissTransportKind}'.")
        };
    }

    private void UpdateSymbolPreview()
    {
        SymbolPreview?.Dispose();
        SymbolPreview = null;
        IsSymbolValid = false;

        if (!TryCreateSymbol(out var symbol))
        {
            return;
        }

        var symbolBitmap = _symbolBitmapProvider.GetSymbolBitmap(symbol);
        SymbolPreview = _previewFactory(symbolBitmap);
        IsSymbolValid = true;
    }

    private void UpdateSelectedSymbolOption()
    {
        SelectedSymbolOption = SymbolOptions.FirstOrDefault(option =>
            option.TableCharacter == SymbolTableCharacter &&
            option.CodeCharacter == SymbolCodeCharacter);
    }

    private static Bitmap CreatePreviewBitmap(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    private bool TryCreateSymbol(out Symbol symbol)
    {
        symbol = default;

        if (SymbolTableCharacter.Length != 1 || SymbolCodeCharacter.Length != 1)
        {
            return false;
        }

        try
        {
            symbol = new Symbol(
                SymbolTableCharacter[0].ToSymbolTable(),
                SymbolCodeCharacter[0].ToSymbolCode());
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

public sealed record AprsSymbolOption(
    string Name,
    string TableCharacter,
    string CodeCharacter);
// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Helpers;
using AetherAprs.Localization;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Services.Ports;
using AetherAprs.Services.UI;
using AetherAprs.Transports.Kiss;
using AetherAprs.ViewModels.Components;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

public partial class AddEditPortViewModel : ViewModelBase, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly IPortService _portService;
    private readonly IBluetoothLeScanner _bleScanner;
    private readonly IBluetoothClassicDeviceProvider _classicDeviceProvider;
    private readonly ILogger<AddEditPortViewModel>? _logger;
    private readonly ITransientFeedbackService? _transientFeedbackService;
    private CancellationTokenSource? _bleScanCts;
    private PortConfig? _existingConfig;
    private bool _isNameManuallyEdited;
    private int _portNumber;

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

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

    /// <summary>
    /// Whether the APRS-IS connection should use TLS with certificate validation.
    /// </summary>
    [ObservableProperty]
    public partial bool UseTls { get; set; }

    /// <summary>
    /// Visual APRS-IS filter builder. Replaces the free-text filter field.
    /// </summary>
    public AprsIsFilterBuilderViewModel FilterBuilder { get; }

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

    /// <summary>
    /// Message shown next to the save action when the port could not be saved.
    /// Kept separate from the Bluetooth scan status so a storage failure never
    /// looks like a scanning problem.
    /// </summary>
    [ObservableProperty]
    public partial string SaveErrorText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the stored APRS-IS passcode could
    /// not be read from the platform secret store. The port still loads, but it is
    /// receive-only until the operator enters the passcode again here.
    /// </summary>
    [ObservableProperty]
    public partial bool HasUnavailableCredentials { get; set; }

    [ObservableProperty]
    public partial string SppStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRx { get; set; } = true;

    [ObservableProperty]
    public partial bool IsTx { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowOnMap { get; set; } = true;

    [ObservableProperty]
    public partial bool AllowDigipeat { get; set; } = true;

    public ObservableCollection<BluetoothLeAdvertisement> BleDevices { get; } = [];

    public ObservableCollection<BluetoothClassicDevice> SppDevices { get; } = [];

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
        IBluetoothClassicDeviceProvider classicDeviceProvider,
        AprsIsFilterBuilderViewModel filterBuilder,
        ILogger<AddEditPortViewModel>? logger = null,
        ITransientFeedbackService? transientFeedbackService = null)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _portService = portService ?? throw new ArgumentNullException(nameof(portService));
        ArgumentNullException.ThrowIfNull(kissStreamFactory);
        _bleScanner = bleScanner ?? throw new ArgumentNullException(nameof(bleScanner));
        _classicDeviceProvider = classicDeviceProvider ?? throw new ArgumentNullException(nameof(classicDeviceProvider));
        FilterBuilder = filterBuilder ?? throw new ArgumentNullException(nameof(filterBuilder));
        _logger = logger;
        _transientFeedbackService = transientFeedbackService;

        IReadOnlyCollection<Type> supported = kissStreamFactory.SupportedTransports;
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
        _portNumber = nextPortNumber;
        OnPropertyChanged(nameof(IsEditing));

        if (existingConfig != null)
        {
            PopulateFrom(existingConfig);
            _isNameManuallyEdited = true; // Existing ports have user-defined names
        }
        else
        {
            _isNameManuallyEdited = false;
            Name = GenerateDefaultPortName();
            Passcode = AprsPasscode.Compute(globalCallsign);
        }

        OnPropertyChanged(nameof(Title));
    }

    private string GenerateDefaultPortName()
    {
        string key = SelectedPortSettingsType == typeof(AprsIsSettings)
            ? "DefaultAprsIsPortName"
            : "DefaultKissPortName";
        return Strings.Format(key, _portNumber);
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
        if (IsSaving)
        {
            return;
        }

        IsSaving = true;
        SaveErrorText = string.Empty;
        try
        {
            StopBleScan();

            if (!TryValidate(out string validationError))
            {
                SaveErrorText = validationError;
                return;
            }

            PortConfig config = BuildConfig();

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
            _transientFeedbackService?.Show(Strings.Get("PortSavedFeedback"));
        }
        catch (Exception ex)
        {
            // Surface the failure instead of silently swallowing it; the user
            // stays on the form and the button remains usable for a retry. The
            // message belongs to the save action, not to the Bluetooth scan status.
            SaveErrorText = Strings.Format("ErrorPrefix", ex.Message);
            _logger?.LogError(ex, "Failed to save port configuration");
        }
        finally
        {
            IsSaving = false;
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSave() => !IsSaving;

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

        // Cancel the previous scan and dispose its CTS only after any in-flight
        // loop has unwound; disposing while the loop still awaits the token throws
        // ObjectDisposedException instead of the expected OperationCanceledException.
        CancellationTokenSource? staleCts = _bleScanCts;
        _bleScanCts = new CancellationTokenSource();
        CancellationToken token = _bleScanCts.Token;
        IsBleScanning = true;

        try
        {
            await foreach (BluetoothLeAdvertisement? advertisement in _bleScanner.ScanAsync(token).ConfigureAwait(true))
            {
                BluetoothLeAdvertisement? existing = BleDevices.FirstOrDefault(device =>
                    string.Equals(device.Address, advertisement.Address, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    // Keep the existing item instance. Android reports the same
                    // peripheral repeatedly with changing RSSI; replacing the
                    // collection item for every advertisement resets the open
                    // ListBox's selection and scroll position.
                    continue;
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

            // Retire our CTS only if this scan is still the active one; the stale
            // CTS is disposed unconditionally now that its loop has unwound.
            if (_bleScanCts is null || !_bleScanCts.Token.Equals(token))
            {
                // This scan was superseded; it owns no live CTS.
            }
            else
            {
                _bleScanCts = null;
            }

            staleCts?.Dispose();
        }
    }

    [RelayCommand]
    private void StopBleScan()
    {
        _bleScanCts?.Cancel();
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
            IReadOnlyList<BluetoothClassicDevice> devices = await _classicDeviceProvider.GetBondedDevicesAsync().ConfigureAwait(true);
            string? selectedAddress = SelectedSppDevice?.Address;

            SppDevices.Clear();
            foreach (BluetoothClassicDevice device in devices)
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

        // Track if user manually edited the name (unless we're initializing)
        if (_portNumber > 0 && !string.IsNullOrEmpty(value))
        {
            string expectedDefault = GenerateDefaultPortName();
            if (value != expectedDefault)
            {
                _isNameManuallyEdited = true;
            }
        }
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

        // Auto-update port name when type changes, but only if user hasn't manually edited it
        if (!_isNameManuallyEdited && _portNumber > 0)
        {
            Name = GenerateDefaultPortName();
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
        AllowDigipeat = config.AllowDigipeat;

        if (config.TypeSettings is AprsIsSettings aprsIs)
        {
            Server = aprsIs.Server;
            ServerPort = aprsIs.ServerPort;

            // A passcode that could not be decrypted is not the operator's secret
            // and must not be shown as if it were; leave the field empty so the
            // re-entry is an explicit action rather than an invisible overwrite.
            HasUnavailableCredentials = config.CredentialsUnavailable;
            Passcode = config.CredentialsUnavailable ? string.Empty : aprsIs.Passcode;
            UseTls = aprsIs.Security == AprsIsSecurityMode.Tls;
            FilterBuilder.Load(aprsIs.Filter);
        }

        if (config.TypeSettings is KissSettings kiss)
        {
            Type? transportType = kiss.Transport?.GetType();
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
                    BluetoothLeAdvertisement bleDevice = new(
                        ble.DeviceAddress,
                        ble.DeviceName,
                        0,
                        [ble.ServiceUuid]);
                    BleDevices.Clear();
                    if (!string.IsNullOrWhiteSpace(ble.DeviceAddress))
                    {
                        BleDevices.Add(bleDevice);
                        SelectedBleDevice = bleDevice;
                    }
                    break;

                case BluetoothClassicKissTransportSettings spp:
                    BluetoothClassicDevice sppDevice = new(spp.DeviceAddress, spp.DeviceName);
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

    /// <summary>
    /// Checks the editable fields in context before anything is persisted, so an
    /// invalid edit is reported with a localized message instead of a raw
    /// configuration exception, and the stored port keeps working unchanged.
    /// </summary>
    private bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = Strings.Get("ValidationPortNameRequired");
            return false;
        }

        if (Name.Length > PortConfig.MaxNameLength)
        {
            error = Strings.Format("ValidationPortNameTooLong", PortConfig.MaxNameLength);
            return false;
        }

        if (SelectedPortSettingsType == typeof(AprsIsSettings))
        {
            return TryValidateAprsIs(out error);
        }

        if (SelectedPortSettingsType == typeof(KissSettings))
        {
            return TryValidateKiss(out error);
        }

        error = string.Empty;
        return true;
    }

    private bool TryValidateAprsIs(out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(Server))
        {
            error = Strings.Get("ValidationServerRequired");
            return false;
        }

        if (Server.Length > AprsIsSettings.MaxServerLength)
        {
            error = Strings.Format("ValidationServerTooLong", AprsIsSettings.MaxServerLength);
            return false;
        }

        if (ServerPort is < 1 or > 65535)
        {
            error = Strings.Format("ValidationPortRange", 1, 65535);
            return false;
        }

        // A credentials failure that the operator has not re-entered keeps the port
        // receive-only, which BuildConfig persists as the anonymous passcode; that is
        // a deliberate choice, not an invalid edit.
        bool credentialsMissing = HasUnavailableCredentials && string.IsNullOrWhiteSpace(Passcode);
        if (!credentialsMissing && string.IsNullOrWhiteSpace(Passcode))
        {
            error = Strings.Get("ValidationPasscodeRequired");
            return false;
        }

        if (Passcode.Length > AprsIsSettings.MaxPasscodeLength)
        {
            error = Strings.Format("ValidationPasscodeTooLong", AprsIsSettings.MaxPasscodeLength);
            return false;
        }

        if (FilterBuilder.Filter.Length > AprsIsSettings.MaxFilterLength)
        {
            error = Strings.Format("ValidationFilterTooLong", AprsIsSettings.MaxFilterLength);
            return false;
        }

        return true;
    }

    private bool TryValidateKiss(out string error)
    {
        error = string.Empty;

        if (SelectedKissTransportType == typeof(TcpKissTransportSettings))
        {
            if (string.IsNullOrWhiteSpace(TcpHost))
            {
                error = Strings.Get("ValidationTcpHostRequired");
                return false;
            }

            if (TcpHost.Length > TcpKissTransportSettings.MaxHostLength)
            {
                error = Strings.Format("ValidationTcpHostTooLong", TcpKissTransportSettings.MaxHostLength);
                return false;
            }

            if (TcpPort is < 1 or > 65535)
            {
                error = Strings.Format("ValidationPortRange", 1, 65535);
                return false;
            }

            return true;
        }

        bool needsBluetoothDevice =
            (SelectedKissTransportType == typeof(BluetoothLeKissTransportSettings) && SelectedBleDevice is null) ||
            (SelectedKissTransportType == typeof(BluetoothClassicKissTransportSettings) && SelectedSppDevice is null);

        if (needsBluetoothDevice)
        {
            error = Strings.Get("ValidationBluetoothDeviceRequired");
            return false;
        }

        return true;
    }

    internal PortConfig BuildConfig()
    {
        PortConfig config = new()
        {
            Name = Name,
            IsRx = IsRx,
            IsTx = IsTx,
            ShowOnMap = ShowOnMap,
            AllowDigipeat = AllowDigipeat
        };

        if (SelectedPortSettingsType == typeof(AprsIsSettings))
        {
            // Saving the form without re-entering a lost passcode keeps the port
            // receive-only: persisting an empty passcode would look configured while
            // every login would be rejected, which is worse than the honest marker.
            bool credentialsMissing = HasUnavailableCredentials && string.IsNullOrWhiteSpace(Passcode);
            config.CredentialsUnavailable = credentialsMissing;
            config.TypeSettings = new AprsIsSettings
            {
                Server = Server,
                ServerPort = ServerPort,
                Passcode = credentialsMissing ? AprsIsSettings.DefaultPasscode : Passcode,
                Filter = FilterBuilder.Filter,
                Security = UseTls ? AprsIsSecurityMode.Tls : AprsIsSecurityMode.Plaintext
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
            Guid serviceUuid = BluetoothLeKissTransportSettings.DefaultServiceUuid;
            Guid rxCharacteristicUuid = BluetoothLeKissTransportSettings.DefaultRxCharacteristicUuid;
            Guid txCharacteristicUuid = BluetoothLeKissTransportSettings.DefaultTxCharacteristicUuid;

            if (SelectedBleDevice?.ServiceUuids.Contains(
                    BluetoothLeKissTransportSettings.LoRaAprsTrackerServiceUuid) == true)
            {
                serviceUuid = BluetoothLeKissTransportSettings.LoRaAprsTrackerServiceUuid;
                rxCharacteristicUuid = BluetoothLeKissTransportSettings.LoRaAprsTrackerRxCharacteristicUuid;
                txCharacteristicUuid = BluetoothLeKissTransportSettings.LoRaAprsTrackerTxCharacteristicUuid;
            }

            return new BluetoothLeKissTransportSettings
            {
                DeviceAddress = SelectedBleDevice?.Address ?? string.Empty,
                DeviceName = SelectedBleDevice?.Name,
                ServiceUuid = serviceUuid,
                RxCharacteristicUuid = rxCharacteristicUuid,
                TxCharacteristicUuid = txCharacteristicUuid
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

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        // Cancel any in-flight BLE scan; its finally block disposes the CTS.
        CancellationTokenSource? cts = _bleScanCts;
        _bleScanCts = null;
        cts?.Cancel();
    }
}

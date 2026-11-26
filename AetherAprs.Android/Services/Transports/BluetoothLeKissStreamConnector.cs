// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Android.Services.Bluetooth;
using AetherAprs.Configuration;
using AetherAprs.Transports.Kiss;
using Android.Bluetooth;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services.Transports;

/// <summary>
/// Connects a KISS byte stream over Bluetooth LE GATT (Nordic UART by default).
/// </summary>
public sealed class BluetoothLeKissStreamConnector : IKissStreamConnector
{
    public Type SettingsType => typeof(BluetoothLeKissTransportSettings);

    public bool IsSupported => true;

    public async Task<Stream> ConnectAsync(IKissTransportSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings is not BluetoothLeKissTransportSettings leSettings)
        {
            throw new ArgumentException(
                $"Expected {nameof(BluetoothLeKissTransportSettings)}, got {settings.GetType().Name}.",
                nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(leSettings.DeviceAddress))
        {
            throw new ArgumentException("Bluetooth LE device address is required.", nameof(settings));
        }

        // GetRemoteDevice throws IllegalArgumentException on a malformed address,
        // so reject it before requesting permissions or touching the adapter.
        string deviceAddress = BluetoothDeviceAddress.Normalize(
            leSettings.DeviceAddress,
            nameof(settings));

        await BluetoothPermissionHelper.EnsurePermissionAsync(cancellationToken).ConfigureAwait(false);

        BluetoothAdapter adapter = BluetoothPermissionHelper.GetAdapterOrThrow();

        if (!adapter.IsEnabled)
        {
            throw new InvalidOperationException("Bluetooth is disabled.");
        }

        BluetoothDevice device = adapter.GetRemoteDevice(deviceAddress)
            ?? throw new InvalidOperationException($"Bluetooth LE device '{deviceAddress}' was not found.");

        return await BluetoothLeGattDuplexStream.ConnectAsync(
            device,
            leSettings.ServiceUuid,
            leSettings.RxCharacteristicUuid,
            leSettings.TxCharacteristicUuid,
            cancellationToken).ConfigureAwait(false);
    }
}

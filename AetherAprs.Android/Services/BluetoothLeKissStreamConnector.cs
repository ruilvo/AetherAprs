// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Transports.Kiss;
using Android.Bluetooth;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services;

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

        await BluetoothPermissionHelper.EnsurePermissionAsync(cancellationToken).ConfigureAwait(false);

        var adapter = BluetoothPermissionHelper.GetAdapterOrThrow();

        if (!adapter.IsEnabled)
        {
            throw new InvalidOperationException("Bluetooth is disabled.");
        }

        var device = adapter.GetRemoteDevice(leSettings.DeviceAddress)
            ?? throw new InvalidOperationException($"Bluetooth LE device '{leSettings.DeviceAddress}' was not found.");

        return await BluetoothLeGattDuplexStream.ConnectAsync(
            device,
            leSettings.ServiceUuid,
            leSettings.RxCharacteristicUuid,
            leSettings.TxCharacteristicUuid,
            cancellationToken).ConfigureAwait(false);
    }
}
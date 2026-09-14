// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Bluetooth;
using Android.Bluetooth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services;

/// <summary>
/// Returns bonded Bluetooth Classic devices for SPP device picker UI.
/// </summary>
public sealed class AndroidBluetoothClassicDeviceProvider : IBluetoothClassicDeviceProvider
{
    public bool IsSupported => true;

    public Task EnsurePermissionAsync(CancellationToken cancellationToken = default) =>
        BluetoothPermissionHelper.EnsurePermissionAsync(cancellationToken);

    public async Task<IReadOnlyList<BluetoothClassicDevice>> GetBondedDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(cancellationToken).ConfigureAwait(false);

        var adapter = BluetoothPermissionHelper.GetAdapterOrThrow();

#pragma warning disable CA1416
        var bonded = adapter.BondedDevices;
#pragma warning restore CA1416

        if (bonded is null || bonded.Count == 0)
        {
            return Array.Empty<BluetoothClassicDevice>();
        }

        return bonded
            .Where(device => device is not null && !string.IsNullOrWhiteSpace(device.Address))
            .Select(device => new BluetoothClassicDevice(device!.Address!, device.Name))
            .OrderBy(device => device.Name ?? device.Address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
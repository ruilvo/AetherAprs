// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Bluetooth;

/// <summary>
/// Placeholder BLE scanner for platforms without Bluetooth LE support.
/// </summary>
public sealed class UnsupportedBluetoothLeScanner : IBluetoothLeScanner
{
    public bool IsSupported => false;

    public Task EnsurePermissionAsync(CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException("Bluetooth LE scanning is not supported on this platform.");
    }

    public IAsyncEnumerable<BluetoothLeAdvertisement> ScanAsync(CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException("Bluetooth LE scanning is not supported on this platform.");
    }
}
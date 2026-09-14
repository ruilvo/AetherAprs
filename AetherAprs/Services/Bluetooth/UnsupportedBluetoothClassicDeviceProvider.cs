// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Bluetooth;

/// <summary>
/// Placeholder Bluetooth Classic device provider for platforms without SPP support.
/// </summary>
public sealed class UnsupportedBluetoothClassicDeviceProvider : IBluetoothClassicDeviceProvider
{
    public bool IsSupported => false;

    public Task EnsurePermissionAsync(CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException("Bluetooth Classic device discovery is not supported on this platform.");
    }

    public Task<IReadOnlyList<BluetoothClassicDevice>> GetBondedDevicesAsync(CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException("Bluetooth Classic device discovery is not supported on this platform.");
    }
}
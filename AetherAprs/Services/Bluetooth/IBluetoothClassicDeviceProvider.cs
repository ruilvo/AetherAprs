// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Bluetooth;

/// <summary>
/// Provides bonded Bluetooth Classic devices for SPP selection.
/// </summary>
public interface IBluetoothClassicDeviceProvider
{
    bool IsSupported { get; }

    Task EnsurePermissionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BluetoothClassicDevice>> GetBondedDevicesAsync(CancellationToken cancellationToken = default);
}
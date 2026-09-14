// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Bluetooth;

/// <summary>
/// Scans for nearby Bluetooth LE peripherals.
/// </summary>
public interface IBluetoothLeScanner
{
    bool IsSupported { get; }

    Task EnsurePermissionAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<BluetoothLeAdvertisement> ScanAsync(CancellationToken cancellationToken = default);
}
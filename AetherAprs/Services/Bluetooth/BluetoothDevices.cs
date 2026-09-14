// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace AetherAprs.Services.Bluetooth;

/// <summary>
/// A BLE advertisement observed during a scan.
/// </summary>
public sealed record BluetoothLeAdvertisement(
    string Address,
    string? Name,
    int Rssi,
    IReadOnlyList<Guid> ServiceUuids);

/// <summary>
/// A bonded Bluetooth Classic device suitable for SPP selection.
/// </summary>
public sealed record BluetoothClassicDevice(
    string Address,
    string? Name);
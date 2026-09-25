// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Data;

/// <summary>
/// Persisted port configuration row.
/// </summary>
public sealed class PortRecord
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public bool IsRx { get; set; } = true;

    public bool IsTx { get; set; } = true;

    public bool ShowOnMap { get; set; } = true;

    /// <summary>
    /// Discriminator for <see cref="AprsIs"/> vs <see cref="Kiss"/>: "aprs-is", "kiss", or null.
    /// </summary>
    public string? Type { get; set; }

    public AprsIsSettingsRecord? AprsIs { get; set; }

    public KissSettingsRecord? Kiss { get; set; }
}

public sealed class AprsIsSettingsRecord
{
    public string Server { get; set; } = string.Empty;

    public int ServerPort { get; set; }

    public string Passcode { get; set; } = string.Empty;

    public string Filter { get; set; } = string.Empty;
}

public sealed class KissSettingsRecord
{
    /// <summary>
    /// Discriminator for the active KISS transport: "tcp", "bt-spp", or "ble".
    /// Required so EF can identify this optional owned type when nested transports exist.
    /// </summary>
    public string Transport { get; set; } = string.Empty;

    public TcpKissTransportRecord? Tcp { get; set; }

    public BluetoothClassicKissTransportRecord? BluetoothClassic { get; set; }

    public BluetoothLeKissTransportRecord? BluetoothLe { get; set; }
}

public sealed class TcpKissTransportRecord
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }
}

public sealed class BluetoothClassicKissTransportRecord
{
    public string DeviceAddress { get; set; } = string.Empty;

    public string? DeviceName { get; set; }
}

public sealed class BluetoothLeKissTransportRecord
{
    public string DeviceAddress { get; set; } = string.Empty;

    public string? DeviceName { get; set; }

    public Guid ServiceUuid { get; set; }

    public Guid RxCharacteristicUuid { get; set; }

    public Guid TxCharacteristicUuid { get; set; }
}

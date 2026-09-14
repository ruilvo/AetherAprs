// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text.Json.Serialization;

namespace AetherAprs.Configuration;

/// <summary>
/// Identifies the byte-stream transport used by a KISS port.
/// </summary>
public enum KissTransportKind
{
    Tcp,
    BluetoothClassic,
    BluetoothLe
}

/// <summary>
/// Marker interface for KISS transport-specific settings.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TcpKissTransportSettings), "tcp")]
[JsonDerivedType(typeof(BluetoothClassicKissTransportSettings), "bt-spp")]
[JsonDerivedType(typeof(BluetoothLeKissTransportSettings), "ble")]
public interface IKissTransportSettings
{
}

/// <summary>
/// TCP KISS transport settings.
/// </summary>
public sealed class TcpKissTransportSettings : IKissTransportSettings
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 8001;

    private string _host = DefaultHost;
    private int _port = DefaultPort;

    public string Host
    {
        get => _host;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Host cannot be empty.", nameof(value));
            }

            _host = value;
        }
    }

    public int Port
    {
        get => _port;
        set
        {
            if (value < 1 || value > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Port must be between 1 and 65535.");
            }

            _port = value;
        }
    }
}

/// <summary>
/// Bluetooth Classic SPP KISS transport settings.
/// </summary>
public sealed class BluetoothClassicKissTransportSettings : IKissTransportSettings
{
    public string DeviceAddress { get; set; } = string.Empty;

    public string? DeviceName { get; set; }
}

/// <summary>
/// Bluetooth LE GATT KISS transport settings.
/// Defaults to Nordic UART Service (NUS) characteristic UUIDs.
/// </summary>
public sealed class BluetoothLeKissTransportSettings : IKissTransportSettings
{
    public static readonly Guid DefaultServiceUuid = Guid.Parse("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    public static readonly Guid DefaultRxCharacteristicUuid = Guid.Parse("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");
    public static readonly Guid DefaultTxCharacteristicUuid = Guid.Parse("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");

    public string DeviceAddress { get; set; } = string.Empty;

    public string? DeviceName { get; set; }

    public Guid ServiceUuid { get; set; } = DefaultServiceUuid;

    /// <summary>
    /// Characteristic written by the host (RX from the peripheral's perspective).
    /// </summary>
    public Guid RxCharacteristicUuid { get; set; } = DefaultRxCharacteristicUuid;

    /// <summary>
    /// Characteristic notified by the peripheral (TX from the peripheral's perspective).
    /// </summary>
    public Guid TxCharacteristicUuid { get; set; } = DefaultTxCharacteristicUuid;
}
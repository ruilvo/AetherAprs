// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text.Json.Serialization;

namespace AetherAprs.Configuration;

/// <summary>
/// Marker interface for KISS transport-specific settings.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TcpKissTransportSettings), "tcp")]
[JsonDerivedType(typeof(BluetoothClassicKissTransportSettings), "bt-spp")]
[JsonDerivedType(typeof(BluetoothLeKissTransportSettings), "ble")]
public interface IKissTransportSettings
{
    /// <summary>Creates an independent copy of these transport settings.</summary>
    IKissTransportSettings Clone();
}

/// <summary>
/// TCP KISS transport settings.
/// </summary>
public sealed class TcpKissTransportSettings : IKissTransportSettings
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 8001;

    /// <summary>
    /// Longest host name that fits the persisted column.
    /// </summary>
    public const int MaxHostLength = 255;

    public string Host
    {
        get;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Host cannot be empty.",
                    nameof(value)
                );
            }

            field = value;
        }
    } = DefaultHost;

    public int Port
    {
        get;
        set
        {
            if (value is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Port must be between 1 and 65535."
                );
            }

            field = value;
        }
    } = DefaultPort;

    /// <inheritdoc />
    public IKissTransportSettings Clone() => new TcpKissTransportSettings { Host = Host, Port = Port };
}

/// <summary>
/// Bluetooth Classic SPP KISS transport settings.
/// </summary>
public sealed class BluetoothClassicKissTransportSettings : IKissTransportSettings
{
    public string DeviceAddress { get; set; } = string.Empty;

    public string? DeviceName { get; set; }

    /// <inheritdoc />
    public IKissTransportSettings Clone() =>
        new BluetoothClassicKissTransportSettings { DeviceAddress = DeviceAddress, DeviceName = DeviceName };
}

/// <summary>
/// Bluetooth LE GATT KISS transport settings.
/// Defaults to Nordic UART Service (NUS) characteristic UUIDs.
/// </summary>
public sealed class BluetoothLeKissTransportSettings : IKissTransportSettings
{
    public static readonly Guid LoRaAprsTrackerServiceUuid =
        Guid.Parse("00000001-BA2A-46C9-AE49-01B0961F68BB");
    public static readonly Guid LoRaAprsTrackerRxCharacteristicUuid =
        Guid.Parse("00000002-BA2A-46C9-AE49-01B0961F68BB");
    public static readonly Guid LoRaAprsTrackerTxCharacteristicUuid =
        Guid.Parse("00000003-BA2A-46C9-AE49-01B0961F68BB");

    public static readonly Guid DefaultServiceUuid =
        Guid.Parse("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    public static readonly Guid DefaultRxCharacteristicUuid =
        Guid.Parse("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");
    public static readonly Guid DefaultTxCharacteristicUuid =
        Guid.Parse("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");

    public string? DeviceName { get; set; }

    public string DeviceAddress { get; set; } = string.Empty;

    public Guid ServiceUuid { get; set; } = DefaultServiceUuid;

    /// <summary>
    /// Characteristic written by the host (RX from the peripheral's perspective).
    /// </summary>
    public Guid RxCharacteristicUuid { get; set; } = DefaultRxCharacteristicUuid;

    /// <summary>
    /// Characteristic notified by the peripheral (TX from the peripheral's perspective).
    /// </summary>
    public Guid TxCharacteristicUuid { get; set; } = DefaultTxCharacteristicUuid;

    /// <inheritdoc />
    public IKissTransportSettings Clone() => new BluetoothLeKissTransportSettings
    {
        DeviceAddress = DeviceAddress,
        DeviceName = DeviceName,
        ServiceUuid = ServiceUuid,
        RxCharacteristicUuid = RxCharacteristicUuid,
        TxCharacteristicUuid = TxCharacteristicUuid
    };
}

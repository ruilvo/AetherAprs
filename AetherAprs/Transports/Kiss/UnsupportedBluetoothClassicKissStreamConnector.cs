// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;

namespace AetherAprs.Transports.Kiss;

/// <summary>
/// Placeholder Bluetooth Classic SPP connector for platforms without SPP support.
/// </summary>
public sealed class UnsupportedBluetoothClassicKissStreamConnector : IKissStreamConnector
{
    /// <inheritdoc />
    public Type SettingsType => typeof(BluetoothClassicKissTransportSettings);

    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public Task<Stream> ConnectAsync(IKissTransportSettings settings, CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException("Bluetooth Classic KISS transport is not supported on this platform.");
    }
}
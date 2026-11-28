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
/// Placeholder Bluetooth LE connector for platforms without BLE GATT support.
/// </summary>
public sealed class UnsupportedBluetoothLeKissStreamConnector : IKissStreamConnector
{
    /// <inheritdoc />
    public KissTransportKind Transport => KissTransportKind.BluetoothLe;

    /// <inheritdoc />
    public Type SettingsType => typeof(BluetoothLeKissTransportSettings);

    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public Task<Stream> ConnectAsync(IKissTransportSettings settings, CancellationToken cancellationToken = default) =>
        ConnectAsync(settings, KissConnectionContext.None, cancellationToken);

    /// <inheritdoc />
    public Task<Stream> ConnectAsync(
        IKissTransportSettings settings,
        KissConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        context.Report(KissConnectionProgress.Failed(
            Transport,
            KissConnectionStage.PreparingTransport,
            "Bluetooth LE KISS transport is not supported on this platform."));

        throw new PlatformNotSupportedException("Bluetooth LE KISS transport is not supported on this platform.");
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Android.Services.Bluetooth;
using AetherAprs.Configuration;
using AetherAprs.Transports.Kiss;

using Android.Bluetooth;

namespace AetherAprs.Android.Services.Transports;

/// <summary>
/// Connects a KISS byte stream over Bluetooth LE GATT (Nordic UART by default).
/// </summary>
public sealed class BluetoothLeKissStreamConnector : IKissStreamConnector
{
    /// <inheritdoc />
    public KissTransportKind Transport => KissTransportKind.BluetoothLe;

    public Type SettingsType => typeof(BluetoothLeKissTransportSettings);

    public bool IsSupported => true;

    /// <inheritdoc />
    public Task<Stream> ConnectAsync(IKissTransportSettings settings, CancellationToken cancellationToken = default) =>
        ConnectAsync(settings, KissConnectionContext.None, cancellationToken);

    public async Task<Stream> ConnectAsync(
        IKissTransportSettings settings,
        KissConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings is not BluetoothLeKissTransportSettings leSettings)
        {
            throw new ArgumentException(
                $"Expected {nameof(BluetoothLeKissTransportSettings)}, got {settings.GetType().Name}.",
                nameof(settings));
        }

        context.Report(Transport, KissConnectionStage.PreparingTransport);

        if (string.IsNullOrWhiteSpace(leSettings.DeviceAddress))
        {
            context.Report(KissConnectionProgress.Failed(
                Transport,
                KissConnectionStage.PreparingTransport,
                "Bluetooth LE device address is required."));

            throw new ArgumentException("Bluetooth LE device address is required.", nameof(settings));
        }

        // GetRemoteDevice throws IllegalArgumentException on a malformed address,
        // so reject it before requesting permissions or touching the adapter.
        string deviceAddress = BluetoothDeviceAddress.Normalize(
            leSettings.DeviceAddress,
            nameof(settings));

        context.Report(Transport, KissConnectionStage.RequestingPermission);

        try
        {
            await BluetoothPermissionHelper.EnsurePermissionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Report(KissConnectionProgress.Cancelled(Transport, KissConnectionStage.RequestingPermission));
            throw;
        }
        catch (Exception ex)
        {
            context.Report(KissConnectionProgress.Failed(
                Transport,
                KissConnectionStage.RequestingPermission,
                ex.Message));
            throw;
        }

        context.Report(Transport, KissConnectionStage.ResolvingDevice);

        BluetoothDevice device;
        try
        {
            BluetoothAdapter adapter = BluetoothPermissionHelper.GetAdapterOrThrow();

            if (!adapter.IsEnabled)
            {
                throw new InvalidOperationException("Bluetooth is disabled.");
            }

            device = adapter.GetRemoteDevice(deviceAddress)
                ?? throw new InvalidOperationException($"Bluetooth LE device '{deviceAddress}' was not found.");
        }
        catch (Exception ex)
        {
            context.Report(KissConnectionProgress.Failed(
                Transport,
                KissConnectionStage.ResolvingDevice,
                ex.Message));
            throw;
        }

        context.Report(Transport, KissConnectionStage.ConnectingTransport);

        try
        {
            return await BluetoothLeGattDuplexStream.ConnectAsync(
                device,
                leSettings.ServiceUuid,
                leSettings.RxCharacteristicUuid,
                leSettings.TxCharacteristicUuid,
                context,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KissConnectionException)
        {
            // The stream already reported the precise GATT stage it stopped at.
            throw;
        }
        catch (Exception ex)
        {
            // A stack exception before the first GATT callback never reached the
            // stream, so the connector reports it at the stage it knows about.
            context.Report(KissConnectionProgress.Failed(
                Transport,
                KissConnectionStage.ConnectingTransport,
                ex.Message));
            throw;
        }
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Transports.Kiss;
using Android.Bluetooth;
using Android.Content;
using Java.Util;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services;

/// <summary>
/// Connects a KISS byte stream over Bluetooth Classic SPP (RFCOMM).
/// </summary>
public sealed class BluetoothClassicKissStreamConnector : IKissStreamConnector
{
    private static readonly UUID SppUuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;

    public KissTransportKind Kind => KissTransportKind.BluetoothClassic;

    public bool IsSupported => true;

    public async Task<Stream> ConnectAsync(IKissTransportSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings is not BluetoothClassicKissTransportSettings classicSettings)
        {
            throw new ArgumentException(
                $"Expected {nameof(BluetoothClassicKissTransportSettings)}, got {settings.GetType().Name}.",
                nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(classicSettings.DeviceAddress))
        {
            throw new ArgumentException("Bluetooth Classic device address is required.", nameof(settings));
        }

        await BluetoothPermissionHelper.EnsurePermissionAsync(cancellationToken).ConfigureAwait(false);

        var adapter = BluetoothPermissionHelper.GetAdapterOrThrow();

        if (!adapter.IsEnabled)
        {
            throw new InvalidOperationException("Bluetooth is disabled.");
        }

        var device = adapter.GetRemoteDevice(classicSettings.DeviceAddress)
            ?? throw new InvalidOperationException($"Bluetooth device '{classicSettings.DeviceAddress}' was not found.");

#pragma warning disable CA1416
        var socket = device.CreateRfcommSocketToServiceRecord(SppUuid)
            ?? throw new InvalidOperationException("Failed to create RFCOMM socket for SPP.");
#pragma warning restore CA1416

        try
        {
            // Cancel discovery to improve connection reliability.
#pragma warning disable CA1416
            if (adapter.IsDiscovering)
            {
                adapter.CancelDiscovery();
            }
#pragma warning restore CA1416

            cancellationToken.ThrowIfCancellationRequested();

            var connectTask = Task.Run(() =>
            {
#pragma warning disable CA1416
                socket.Connect();
#pragma warning restore CA1416
            }, cancellationToken);

            await using (cancellationToken.Register(() =>
            {
                try
                {
                    socket.Close();
                }
                catch
                {
                    // Best-effort cancel.
                }
            }))
            {
                await connectTask.ConfigureAwait(false);
            }

            if (!socket.IsConnected)
            {
                throw new IOException($"Failed to connect to Bluetooth Classic device '{classicSettings.DeviceAddress}'.");
            }

            return new BluetoothSocketDuplexStream(socket);
        }
        catch
        {
            try
            {
                socket.Close();
            }
            catch
            {
                // Ignore close failures while propagating the original error.
            }

            socket.Dispose();
            throw;
        }
    }
}
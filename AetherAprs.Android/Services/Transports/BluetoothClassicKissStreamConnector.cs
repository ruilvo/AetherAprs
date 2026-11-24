// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Android.Services.Bluetooth;
using AetherAprs.Configuration;
using AetherAprs.Transports.Kiss;
using Java.Util;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services.Transports;

/// <summary>
/// Connects a KISS byte stream over Bluetooth Classic SPP (RFCOMM).
/// </summary>
public sealed class BluetoothClassicKissStreamConnector : IKissStreamConnector
{
    private static readonly UUID SppUuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;

    /// <summary>
    /// Upper bound for the blocking RFCOMM connect. Some stacks report a
    /// connection failure only after 10–30 seconds, and a hung peer can block
    /// indefinitely; the socket is closed when this bound expires (audit P-19).
    /// </summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    public Type SettingsType => typeof(BluetoothClassicKissTransportSettings);

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

        // Validate before requesting permissions or touching the adapter:
        // GetRemoteDevice throws IllegalArgumentException on a malformed address.
        var deviceAddress = BluetoothDeviceAddress.Normalize(
            classicSettings.DeviceAddress,
            nameof(settings));

        await BluetoothPermissionHelper.EnsurePermissionAsync(cancellationToken).ConfigureAwait(false);

        var adapter = BluetoothPermissionHelper.GetAdapterOrThrow();

        if (!adapter.IsEnabled)
        {
            throw new InvalidOperationException("Bluetooth is disabled.");
        }

        var device = adapter.GetRemoteDevice(deviceAddress)
            ?? throw new InvalidOperationException($"Bluetooth device '{deviceAddress}' was not found.");
        var socket = device.CreateRfcommSocketToServiceRecord(SppUuid)
            ?? throw new InvalidOperationException("Failed to create RFCOMM socket for SPP.");
        try
        {
            // Cancel discovery to improve connection reliability.
            if (adapter.IsDiscovering)
            {
                adapter.CancelDiscovery();
            }

            cancellationToken.ThrowIfCancellationRequested();

            await using (cancellationToken.Register(() => CloseSocket(socket)))
            {
                await BlockingConnectTimeout
                    .RunAsync(socket.Connect, ConnectTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!socket.IsConnected)
            {
                throw new IOException($"Failed to connect to Bluetooth Classic device '{deviceAddress}'.");
            }

            return new BluetoothSocketDuplexStream(socket);
        }
        catch
        {
            // The wait was bounded or cancelled while Connect() may still be
            // blocked; closing the socket releases the native handle and lets
            // the platform call fail instead of leaking a connection attempt.
            CloseSocket(socket);
            socket.Dispose();
            throw;
        }
    }

    private static void CloseSocket(global::Android.Bluetooth.BluetoothSocket socket)
    {
        try
        {
            socket.Close();
        }
        catch
        {
            // Best-effort cancel/cleanup.
        }
    }
}

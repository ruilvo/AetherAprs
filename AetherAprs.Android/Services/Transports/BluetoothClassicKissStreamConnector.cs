// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Android.Services.Bluetooth;
using AetherAprs.Configuration;
using AetherAprs.Transports.Kiss;
using Android.Bluetooth;
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
    /// indefinitely; the socket is closed when this bound expires.
    /// </summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    /// <inheritdoc />
    public KissTransportKind Transport => KissTransportKind.BluetoothClassicSpp;

    public Type SettingsType => typeof(BluetoothClassicKissTransportSettings);

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

        if (settings is not BluetoothClassicKissTransportSettings classicSettings)
        {
            throw new ArgumentException(
                $"Expected {nameof(BluetoothClassicKissTransportSettings)}, got {settings.GetType().Name}.",
                nameof(settings));
        }

        context.Report(Transport, KissConnectionStage.PreparingTransport);

        if (string.IsNullOrWhiteSpace(classicSettings.DeviceAddress))
        {
            context.Report(KissConnectionProgress.Failed(
                Transport,
                KissConnectionStage.PreparingTransport,
                "Bluetooth Classic device address is required."));

            throw new ArgumentException("Bluetooth Classic device address is required.", nameof(settings));
        }

        // Validate before requesting permissions or touching the adapter:
        // GetRemoteDevice throws IllegalArgumentException on a malformed address.
        string deviceAddress = BluetoothDeviceAddress.Normalize(
            classicSettings.DeviceAddress,
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
        BluetoothAdapter adapter;
        try
        {
            adapter = BluetoothPermissionHelper.GetAdapterOrThrow();

            if (!adapter.IsEnabled)
            {
                throw new InvalidOperationException("Bluetooth is disabled.");
            }

            device = adapter.GetRemoteDevice(deviceAddress)
                ?? throw new InvalidOperationException($"Bluetooth device '{deviceAddress}' was not found.");
        }
        catch (Exception ex)
        {
            context.Report(KissConnectionProgress.Failed(
                Transport,
                KissConnectionStage.ResolvingDevice,
                ex.Message));
            throw;
        }

        BluetoothSocket socket = device.CreateRfcommSocketToServiceRecord(SppUuid)
            ?? throw new InvalidOperationException("Failed to create RFCOMM socket for SPP.");
        try
        {
            // Cancel discovery to improve connection reliability.
            if (adapter.IsDiscovering)
            {
                adapter.CancelDiscovery();
            }

            cancellationToken.ThrowIfCancellationRequested();

            context.Report(KissConnectionProgress.Progress(
                Transport,
                KissConnectionStage.ConnectingTransport) with
            {
                SppLinkState = KissSppLinkState.Connecting
            });

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

            context.Report(KissConnectionProgress.Ready(
                Transport,
                sppLinkState: KissSppLinkState.Connected));

            return new BluetoothSocketDuplexStream(socket);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The wait was bounded or cancelled while Connect() may still be
            // blocked; closing the socket releases the native handle and lets
            // the platform call fail instead of leaking a connection attempt.
            CloseSocket(socket);
            socket.Dispose();
            context.Report(KissConnectionProgress.Cancelled(
                Transport,
                KissConnectionStage.ConnectingTransport,
                KissSppLinkState.Connecting));
            throw;
        }
        catch (Exception ex)
        {
            CloseSocket(socket);
            socket.Dispose();
            context.Report(KissConnectionProgress.Failed(
                Transport,
                KissConnectionStage.ConnectingTransport,
                ex.Message) with
            {
                SppLinkState = KissSppLinkState.Connecting
            });
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

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Bluetooth;
using Android.Bluetooth.LE;
using Android.Runtime;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services;

/// <summary>
/// Android BLE scanner that yields live advertisements (not bonded-device lists).
/// </summary>
public sealed class AndroidBluetoothLeScanner : IBluetoothLeScanner
{
    public bool IsSupported => true;

    public Task EnsurePermissionAsync(CancellationToken cancellationToken = default) =>
        BluetoothPermissionHelper.EnsurePermissionAsync(cancellationToken);

    public async IAsyncEnumerable<BluetoothLeAdvertisement> ScanAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(cancellationToken).ConfigureAwait(false);

        var adapter = BluetoothPermissionHelper.GetAdapterOrThrow();

        if (!adapter.IsEnabled)
        {
            throw new InvalidOperationException("Bluetooth is disabled.");
        }

        var scanner = adapter.BluetoothLeScanner
            ?? throw new InvalidOperationException("Bluetooth LE scanner is not available on this device.");
        var channel = Channel.CreateUnbounded<BluetoothLeAdvertisement>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        var callback = new AdvertisementScanCallback(advertisement =>
        {
            channel.Writer.TryWrite(advertisement);
        });
        var settings = new ScanSettings.Builder()
            .SetScanMode(ScanMode.LowLatency)!
            .Build();

        scanner.StartScan(filters: null, settings, callback);
        try
        {
            await foreach (var advertisement in channel.Reader.ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return advertisement;
            }
        }
        finally
        {
            try
            {
                scanner.StopScan(callback);
            }
            catch
            {
                // Best-effort stop.
            }

            callback.Dispose();
            channel.Writer.TryComplete();
        }
    }

    private sealed class AdvertisementScanCallback(Action<BluetoothLeAdvertisement> onAdvertisement) : ScanCallback
    {
        public override void OnScanResult([GeneratedEnum] ScanCallbackType callbackType, ScanResult? result)
        {
            Publish(result);
        }

        public override void OnBatchScanResults(IList<ScanResult>? results)
        {
            if (results is null)
            {
                return;
            }

            foreach (var result in results)
            {
                Publish(result);
            }
        }

        public override void OnScanFailed([GeneratedEnum] ScanFailure errorCode)
        {
            // Surface as completed channel; callers observe no further ads.
        }

        private void Publish(ScanResult? result)
        {
            if (result?.Device?.Address is not string address || string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            var serviceUuids = new List<Guid>();
            var parcelUuids = result.ScanRecord?.ServiceUuids;
            if (parcelUuids is not null)
            {
                foreach (var parcelUuid in parcelUuids)
                {
                    var uuid = parcelUuid?.Uuid;
                    if (uuid is null)
                    {
                        continue;
                    }

                    if (Guid.TryParse(uuid.ToString(), out var guid))
                    {
                        serviceUuids.Add(guid);
                    }
                }
            }

            var name = result.ScanRecord?.DeviceName ?? result.Device.Name;
            var rssi = result.Rssi;
            onAdvertisement(new BluetoothLeAdvertisement(address, name, rssi, serviceUuids));
        }
    }
}
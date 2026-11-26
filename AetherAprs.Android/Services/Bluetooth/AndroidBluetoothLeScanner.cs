// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Bluetooth;
using Android.Bluetooth.LE;
using Android.OS;
using Android.Runtime;
using Java.Util;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services.Bluetooth;

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

        global::Android.Bluetooth.BluetoothAdapter adapter = BluetoothPermissionHelper.GetAdapterOrThrow();

        if (!adapter.IsEnabled)
        {
            throw new InvalidOperationException("Bluetooth is disabled.");
        }

        BluetoothLeScanner scanner = adapter.BluetoothLeScanner
            ?? throw new InvalidOperationException("Bluetooth LE scanner is not available on this device.");
        // Filter by advertised service here rather than at the platform: a
        // ScanFilter would also hide devices that only reveal their GATT services
        // after connecting, which is how most KISS-over-BLE bridges appear.
        BluetoothAdvertisementBuffer buffer = new(
            filter: BluetoothLeServiceFilter.SupportedTransports);

        AdvertisementScanCallback callback = new(advertisement => buffer.Publish(advertisement),
        error => buffer.Complete(new InvalidOperationException($"BLE scan failed: {error}")));
        ScanSettings? settings = new ScanSettings.Builder()
            .SetScanMode(ScanMode.LowLatency)!
            .Build();

        scanner.StartScan(filters: null, settings, callback);
        try
        {
            await foreach (BluetoothLeAdvertisement? advertisement in buffer.Reader.ReadAllAsync(cancellationToken)
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
            buffer.Complete();
        }
    }

    private sealed class AdvertisementScanCallback(
        Action<BluetoothLeAdvertisement> onAdvertisement,
        Action<ScanFailure> onScanFailed) : ScanCallback
    {
        public override void OnScanResult([GeneratedEnum] ScanCallbackType callbackType, ScanResult? result) => Publish(result);

        public override void OnBatchScanResults(IList<ScanResult>? results)
        {
            if (results is null)
            {
                return;
            }

            foreach (ScanResult result in results)
            {
                Publish(result);
            }
        }

        public override void OnScanFailed([GeneratedEnum] ScanFailure errorCode) =>
            // Complete the channel with an error so callers observe the failure
            // instead of a scan that "succeeds" with zero results.
            onScanFailed(errorCode);

        private void Publish(ScanResult? result)
        {
            if (result?.Device?.Address is not string address || string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            List<Guid> serviceUuids = [];
            IList<ParcelUuid>? parcelUuids = result.ScanRecord?.ServiceUuids;
            if (parcelUuids is not null)
            {
                foreach (ParcelUuid parcelUuid in parcelUuids)
                {
                    UUID? uuid = parcelUuid?.Uuid;
                    if (uuid is null)
                    {
                        continue;
                    }

                    if (Guid.TryParse(uuid.ToString(), out Guid guid))
                    {
                        serviceUuids.Add(guid);
                    }
                }
            }

            string? name = result.ScanRecord?.DeviceName ?? result.Device.Name;
            int rssi = result.Rssi;
            onAdvertisement(new BluetoothLeAdvertisement(address, name, rssi, serviceUuids));
        }
    }
}

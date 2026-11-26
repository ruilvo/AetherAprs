// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using AetherAprs.Configuration;

namespace AetherAprs.Services.Bluetooth;

/// <summary>
/// Decides whether an advertisement is worth showing to the operator while
/// picking a Bluetooth LE port.
/// </summary>
/// <remarks>
/// A scan sees every peripheral in range, from headphones to heart-rate straps.
/// Keeping only the advertisements that name a service this application can speak
/// to trims that list without hiding anything usable.
///
/// The rule deliberately tolerates an advertisement that names no service at all.
/// GATT services are often absent from the advertisement and only appear after
/// connecting, so dropping those entries would empty the picker on exactly the
/// devices the operator is looking for. Only a device that advertises services and
/// none of the supported ones is rejected, because in that case the advertisement
/// itself says this is not a supported transport.
/// </remarks>
public sealed class BluetoothLeServiceFilter
{
    private readonly HashSet<Guid> _supportedServiceUuids;

    /// <summary>
    /// Creates a filter that keeps advertisements naming at least one of
    /// <paramref name="supportedServiceUuids"/>.
    /// </summary>
    /// <param name="supportedServiceUuids">
    /// The service identifiers this application can connect to.
    /// </param>
    public BluetoothLeServiceFilter(IEnumerable<Guid> supportedServiceUuids)
    {
        ArgumentNullException.ThrowIfNull(supportedServiceUuids);

        _supportedServiceUuids = [.. supportedServiceUuids];
    }

    /// <summary>
    /// A filter for the transports this application implements: the Nordic UART
    /// service used by KISS over Bluetooth LE and the LoRa APRS tracker service.
    /// </summary>
    public static BluetoothLeServiceFilter SupportedTransports { get; } = new(
    [
        BluetoothLeKissTransportSettings.DefaultServiceUuid,
        BluetoothLeKissTransportSettings.LoRaAprsTrackerServiceUuid
    ]);

    /// <summary>
    /// Tests one advertisement against the supported services.
    /// </summary>
    /// <param name="advertisement">The advertisement to judge.</param>
    /// <returns>
    /// <see langword="true"/> when the advertisement names a supported service or
    /// names no service at all; otherwise <see langword="false"/>.
    /// </returns>
    public bool Accepts(BluetoothLeAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(advertisement);

        if (advertisement.ServiceUuids.Count == 0)
        {
            return true;
        }

        foreach (Guid serviceUuid in advertisement.ServiceUuids)
        {
            if (_supportedServiceUuids.Contains(serviceUuid))
            {
                return true;
            }
        }

        return false;
    }
}

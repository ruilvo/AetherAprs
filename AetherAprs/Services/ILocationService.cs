// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services;

/// <summary>
/// Provides access to the device's location services.
/// </summary>
public interface ILocationService
{
    /// <summary>
    /// Gets the current location of the device.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the location data.</returns>
    /// <exception cref="InvalidOperationException">Thrown when location services are not available or permission is denied.</exception>
    Task<LocationData> GetCurrentLocationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if location services are available on the device.
    /// </summary>
    /// <returns>True if location services are available; otherwise, false.</returns>
    bool IsLocationAvailable();

    /// <summary>
    /// Requests location permission from the user if not already granted.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result indicates whether permission was granted.</returns>
    Task<bool> RequestLocationPermissionAsync();
}

/// <summary>
/// Represents location data from the device.
/// </summary>
public record LocationData
{
    /// <summary>
    /// Gets the latitude in decimal degrees.
    /// </summary>
    public double Latitude { get; init; }

    /// <summary>
    /// Gets the longitude in decimal degrees.
    /// </summary>
    public double Longitude { get; init; }

    /// <summary>
    /// Gets the altitude in meters above sea level, if available.
    /// </summary>
    public double? Altitude { get; init; }

    /// <summary>
    /// Gets the accuracy of the location in meters.
    /// </summary>
    public double Accuracy { get; init; }

    /// <summary>
    /// Gets the timestamp when the location was obtained.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}

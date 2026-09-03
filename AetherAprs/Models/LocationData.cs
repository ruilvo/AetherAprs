// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Models;

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

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Geo;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;
using System.Globalization;

namespace AetherAprs.Data.Converters;

/// <summary>
/// Converts Geo.Coordinate to/from string for EF Core storage.
/// Stores as "latitude,longitude" format.
/// </summary>
public sealed class CoordinateValueConverter : ValueConverter<Coordinate, string>
{
    public CoordinateValueConverter()
        : base(
            coord => coord.Latitude.ToString(CultureInfo.InvariantCulture) + "," + coord.Longitude.ToString(CultureInfo.InvariantCulture),
            str => ParseCoordinate(str))
    {
    }

    private static Coordinate ParseCoordinate(string value)
    {
        // Defensive parse: one malformed Location string in the database must not
        // kill every position query during materialization (M6). Falls back to a
        // (0,0) coordinate, which will simply render at the equator if ever shown.
        var parts = value.Split(',');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
        {
            return new Coordinate(latitude, longitude);
        }

        return new Coordinate(0, 0);
    }
}

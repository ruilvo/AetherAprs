// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Geo;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;
using System.Globalization;

namespace AetherAprs.Data;

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
        var parts = value.Split(',');
        if (parts.Length != 2)
        {
            throw new FormatException($"Invalid coordinate format: {value}");
        }

        var latitude = double.Parse(parts[0], CultureInfo.InvariantCulture);
        var longitude = double.Parse(parts[1], CultureInfo.InvariantCulture);
        return new Coordinate(latitude, longitude);
    }
}

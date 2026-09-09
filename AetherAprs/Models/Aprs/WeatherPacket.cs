// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs;

/// <summary>
/// An APRS weather report packet (format: _...).
/// </summary>
public sealed record WeatherPacket : AprsPacket
{
    /// <inheritdoc />
    public override PacketType Type => PacketType.Weather;

    /// <summary>Wind direction in degrees (0-359), if available.</summary>
    public double? WindDirection { get; init; }

    /// <summary>Wind speed in knots, if available.</summary>
    public double? WindSpeed { get; init; }

    /// <summary>Wind gust in knots, if available.</summary>
    public double? WindGust { get; init; }

    /// <summary>Temperature in degrees Fahrenheit, if available.</summary>
    public double? Temperature { get; init; }

    /// <summary>Humidity as a percentage (0-100), if available.</summary>
    public double? Humidity { get; init; }

    /// <summary>Barometric pressure in millibars (hPa), if available.</summary>
    public double? Pressure { get; init; }

    /// <summary>Rainfall in the last 1 hour in inches, if available.</summary>
    public double? Rain1h { get; init; }

    /// <summary>Rainfall in the last 24 hours in inches, if available.</summary>
    public double? Rain24h { get; init; }
}
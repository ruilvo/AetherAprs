// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs;

/// <summary>
/// An APRS position report packet.
/// </summary>
public sealed record PositionPacket : AprsPacket
{
    /// <inheritdoc />
    public override PacketType Type => PacketType.Position;

    /// <summary>
    /// Gets the latitude in decimal degrees.
    /// </summary>
    public double Latitude { get; init; }

    /// <summary>
    /// Gets the longitude in decimal degrees.
    /// </summary>
    public double Longitude { get; init; }

    /// <summary>
    /// Gets the symbol for this position.
    /// </summary>
    public Symbol Symbol { get; init; }

    /// <summary>
    /// Gets the comment text after the position data, if any.
    /// </summary>
    public string? Comment { get; init; }

    /// <summary>
    /// Gets the course in degrees (0-359), if available.
    /// </summary>
    public double? Course { get; init; }

    /// <summary>
    /// Gets the speed in knots, if available.
    /// </summary>
    public double? Speed { get; init; }

    /// <summary>
    /// Gets the altitude in feet, if available.
    /// </summary>
    public double? Altitude { get; init; }

    /// <summary>
    /// Gets the number of decimal digits of minute precision (0-3).
    /// </summary>
    public int Precision { get; init; }
}
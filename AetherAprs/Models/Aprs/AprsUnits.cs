// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Unit conversions and wire encodings used by the packet layer.
/// </summary>
/// <remarks>
/// The domain model keeps what the APRS specification carries and what the operator
/// reads: positions in degrees, altitude in feet, speed in knots. Conversions live
/// here with a name instead of a bare factor so a reader never has to guess whether
/// a number is metres, feet, knots or kilometres per hour, and so the encoded forms
/// used by compressed positions have one implementation instead of a matching pair
/// that can drift apart.
/// </remarks>
public static class AprsUnits
{
    /// <summary>Feet in one metre (international foot).</summary>
    public const double FeetPerMeter = 3.280839895013123;

    /// <summary>Metres per second in one knot (one nautical mile per hour).</summary>
    public const double MetersPerSecondPerKnot = 0.5144444444444445;

    /// <summary>Kilometres per hour in one knot.</summary>
    public const double KilometersPerHourPerKnot = 1.852;

    private const double CompressedAltitudeBase = 1.002;
    private const double CompressedSpeedBase = 1.08;

    /// <summary>Converts metres to international feet.</summary>
    /// <param name="meters">A length in metres.</param>
    /// <returns>The same length in feet.</returns>
    public static double MetersToFeet(double meters) => meters * FeetPerMeter;

    /// <summary>Converts international feet to metres.</summary>
    /// <param name="feet">A length in feet.</param>
    /// <returns>The same length in metres.</returns>
    public static double FeetToMeters(double feet) => feet / FeetPerMeter;

    /// <summary>Converts knots to metres per second.</summary>
    /// <param name="knots">A speed in knots.</param>
    /// <returns>The same speed in metres per second.</returns>
    public static double KnotsToMetersPerSecond(double knots) => knots * MetersPerSecondPerKnot;

    /// <summary>Converts metres per second to knots.</summary>
    /// <param name="metersPerSecond">A speed in metres per second.</param>
    /// <returns>The same speed in knots.</returns>
    public static double MetersPerSecondToKnots(double metersPerSecond) => metersPerSecond / MetersPerSecondPerKnot;

    /// <summary>Converts knots to kilometres per hour.</summary>
    /// <param name="knots">A speed in knots.</param>
    /// <returns>The same speed in kilometres per hour.</returns>
    public static double KnotsToKilometersPerHour(double knots) => knots * KilometersPerHourPerKnot;

    /// <summary>Converts metres per second to kilometres per hour.</summary>
    /// <param name="metersPerSecond">A speed in metres per second.</param>
    /// <returns>The same speed in kilometres per hour.</returns>
    public static double MetersPerSecondToKilometersPerHour(double metersPerSecond) =>
        KnotsToKilometersPerHour(MetersPerSecondToKnots(metersPerSecond));

    /// <summary>
    /// Decodes the compressed-position altitude, which the specification carries as
    /// base-1.002 logarithm of feet split over the two course/speed characters.
    /// </summary>
    /// <param name="c1">The first character value, already offset by 33.</param>
    /// <param name="s1">The second character value, already offset by 33.</param>
    /// <returns>The altitude in feet.</returns>
    public static double DecodeCompressedAltitude(int c1, int s1) =>
        Math.Pow(CompressedAltitudeBase, (c1 * 91) + s1);

    /// <summary>
    /// Encodes an altitude for a compressed position.
    /// </summary>
    /// <param name="feet">The altitude in feet; must be positive.</param>
    /// <returns>
    /// The pair of character values (before the 33 offset) whose base-1.002
    /// representation is nearest to <paramref name="feet"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="feet"/> is not positive, which the encoding cannot represent.
    /// </exception>
    public static (int C1, int S1) EncodeCompressedAltitude(double feet)
    {
        if (feet <= 0 || double.IsNaN(feet) || double.IsInfinity(feet))
        {
            throw new ArgumentOutOfRangeException(
                nameof(feet), feet, "Compressed position altitude must be a positive number of feet.");
        }

        var combined = (int)Math.Round(
            Math.Log(feet) / Math.Log(CompressedAltitudeBase),
            MidpointRounding.AwayFromZero);
        return (combined / 91, combined % 91);
    }

    /// <summary>
    /// Decodes the compressed-position speed, which the specification carries as
    /// 1.08^s1 - 1 knots.
    /// </summary>
    /// <param name="s1">The second character value, already offset by 33.</param>
    /// <returns>The speed in knots.</returns>
    public static double DecodeCompressedSpeed(int s1) => Math.Pow(CompressedSpeedBase, s1) - 1.0;

    /// <summary>
    /// Encodes a speed for a compressed position.
    /// </summary>
    /// <param name="knots">The speed in knots; must not be negative.</param>
    /// <returns>
    /// The character value (before the 33 offset) whose 1.08^s1 - 1 representation is
    /// nearest to <paramref name="knots"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="knots"/> is negative, which the encoding cannot represent.
    /// </exception>
    public static int EncodeCompressedSpeed(double knots)
    {
        if (knots < 0 || double.IsNaN(knots) || double.IsInfinity(knots))
        {
            throw new ArgumentOutOfRangeException(
                nameof(knots), knots, "Compressed position speed must be a non-negative number of knots.");
        }

        return (int)Math.Round(
            Math.Log(knots + 1.0) / Math.Log(CompressedSpeedBase),
            MidpointRounding.AwayFromZero);
    }
}

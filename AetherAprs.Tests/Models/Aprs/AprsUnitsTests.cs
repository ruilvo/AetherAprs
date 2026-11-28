// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.Models.Aprs;

using Xunit;

namespace AetherAprs.Tests.Models.Aprs;

public sealed class AprsUnitsTests
{
    [Fact]
    public void MetersToFeet_UsesTheInternationalFoot()
    {
        Assert.Equal(0, AprsUnits.MetersToFeet(0));
        Assert.Equal(3280.839895013123, AprsUnits.MetersToFeet(1000), 9);
    }

    [Fact]
    public void FeetToMeters_ReversesMetersToFeet() => Assert.Equal(1234.5, AprsUnits.FeetToMeters(AprsUnits.MetersToFeet(1234.5)), 9);

    [Fact]
    public void KnotsToMetersPerSecond_UsesOneNauticalMilePerHour()
    {
        Assert.Equal(0, AprsUnits.KnotsToMetersPerSecond(0));
        Assert.Equal(51.44444444444444, AprsUnits.KnotsToMetersPerSecond(100), 9);
        Assert.Equal(100, AprsUnits.MetersPerSecondToKnots(AprsUnits.KnotsToMetersPerSecond(100)), 9);
    }

    [Fact]
    public void KnotsToKilometersPerHour_UsesTheNauticalMile()
    {
        Assert.Equal(18.52, AprsUnits.KnotsToKilometersPerHour(10), 9);
        Assert.Equal(185.2, AprsUnits.KnotsToKilometersPerHour(100), 9);
    }

    [Fact]
    public void MetersPerSecondToKilometersPerHour_MatchesTheDirectConversion()
    {
        Assert.Equal(36, AprsUnits.MetersPerSecondToKilometersPerHour(10), 9);
        // One knot is 1852 m in 3600 s.
        Assert.Equal(
            AprsUnits.KnotsToKilometersPerHour(1),
            AprsUnits.MetersPerSecondToKilometersPerHour(1852.0 / 3600.0),
            9);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    [InlineData(30000)]
    public void CompressedAltitude_RoundTripsWithinOneEncodingStep(double feet)
    {
        (int c1, int s1) = AprsUnits.EncodeCompressedAltitude(feet);

        double decoded = AprsUnits.DecodeCompressedAltitude(c1, s1);

        // Each step of the base-1.002 logarithm is 0.2 % of the altitude, so a
        // round trip may move by at most one step.
        Assert.InRange(decoded, feet * 0.998, feet * 1.002);
        Assert.InRange(c1, 0, 90);
        Assert.InRange(s1, 0, 90);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(200)]
    public void CompressedSpeed_RoundTripsWithinOneEncodingStep(double knots)
    {
        int s1 = AprsUnits.EncodeCompressedSpeed(knots);

        double decoded = AprsUnits.DecodeCompressedSpeed(s1);

        // The step grows with the speed (8 % of value + 1 knot), so compare against
        // the neighbouring encodings instead of a fixed tolerance.
        double lower = AprsUnits.DecodeCompressedSpeed(Math.Max(0, s1 - 1));
        double upper = AprsUnits.DecodeCompressedSpeed(s1 + 1);
        Assert.InRange(decoded, lower, upper);
        Assert.InRange(decoded, knots - ((upper - lower) / 2), knots + ((upper - lower) / 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EncodeCompressedAltitude_ForANonPositiveAltitude_Throws(double feet) => Assert.Throws<ArgumentOutOfRangeException>(() => AprsUnits.EncodeCompressedAltitude(feet));

    [Fact]
    public void EncodeCompressedSpeed_ForANegativeSpeed_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() => AprsUnits.EncodeCompressedSpeed(-0.5));
}

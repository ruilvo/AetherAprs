// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Aprs;
using Xunit;

namespace AetherAprs.Tests.Modems.Aprs;

/// <summary>
/// Comprehensive tests for APRS position encoding/decoding to verify
/// latitude/longitude accuracy across all quadrants and encoding formats.
/// </summary>
public class PositionEncodingTests
{
    /// <summary>
    /// Latitude values covering every quadrant, the poles, and the region where
    /// the compressed-format base-91 value is small (high north latitudes). The
    /// high-latitude cases are regressions for a decoder that mis-handled small
    /// base-91 values and produced wrong or negative latitudes.
    /// </summary>
    private static readonly double[] LatitudeValues =
    [
        -90.0, -89.99, -89.0, -75.0, -60.0, -45.0, -33.5, -23.5, -10.0, -0.01,
        0.0,
        0.01, 10.0, 23.5, 33.5, 45.0, 60.0, 75.0, 85.0, 88.9, 88.95, 89.0,
        89.5, 89.99, 90.0,
    ];

    /// <summary>
    /// Longitude values covering every quadrant, the date line, and the region
    /// where the compressed-format base-91 value is small (far west). The far
    /// west cases are regressions for a decoder that mis-handled small base-91
    /// values and produced wrong longitudes.
    /// </summary>
    private static readonly double[] LongitudeValues =
    [
        -180.0, -179.99, -179.5, -178.95, -170.0, -120.0, -74.0, -46.6, -10.0,
        -0.01,
        0.0,
        0.01, 10.0, 45.5, 74.0, 100.0, 120.0, 151.2, 170.0, 178.95, 179.5,
        179.99, 180.0,
    ];

    /// <summary>
    /// Longitudes representable by Mic-E (|lon| &lt;= 179.99); Mic-E cannot encode
    /// an exact 180° longitude.
    /// </summary>
    private static readonly double[] MicELongitudeValues =
    [
        -179.99, -179.5, -178.95, -170.0, -120.0, -74.0, -46.6, -10.0, -0.01,
        0.0,
        0.01, 10.0, 45.5, 74.0, 100.0, 120.0, 151.2, 170.0, 178.95, 179.5,
        179.99,
    ];

    public static TheoryData<double, double> CompressedCoordinates
    {
        get
        {
            var data = new TheoryData<double, double>();
            foreach (var lat in LatitudeValues)
            {
                foreach (var lon in LongitudeValues)
                {
                    data.Add(lat, lon);
                }
            }

            return data;
        }
    }

    public static TheoryData<double, double> MicECoordinates
    {
        get
        {
            var data = new TheoryData<double, double>();
            foreach (var lat in LatitudeValues)
            {
                foreach (var lon in MicELongitudeValues)
                {
                    data.Add(lat, lon);
                }
            }

            return data;
        }
    }

    // ---------------------------------------------------------------
    // Compressed position tests
    // ---------------------------------------------------------------

    [Fact]
    public void CompressedPosition_KnownVector_DecodesCorrectly()
    {
        // 42.2419°N, 8.59665°W encoded as compressed base-91.
        var result = AprsInfoFieldParser.ParseInfoField(
            "!/9-pFL>:4uBkQ",
            new Callsign("N0CALL"),
            new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(42.2419, pos.Location.Latitude, 4);
        Assert.Equal(-8.59665, pos.Location.Longitude, 5);
    }

    [Theory]
    [MemberData(nameof(CompressedCoordinates))]
    public void CompressedPosition_RoundTrip_PreservesCoordinates(double lat, double lon)
    {
        // Compressed base-91 resolution is 1/380926° (lat) and 1/190463° (lon),
        // so a correct round trip is accurate to about 3e-6°. A tolerance of 5
        // decimal places (1e-5°) proves the decoder uses the right formula.
        var packet = CreatePositionPacket(lat, lon, PositionFormat.Compressed);

        var infoField = AprsInfoFieldSerializer.FormatInfoField(packet);
        var decoded = Assert.IsType<PositionPacket>(
            AprsInfoFieldParser.ParseInfoField(infoField, packet.Source, packet.Destination));

        Assert.Equal(lat, decoded.Location.Latitude, 5);
        Assert.Equal(lon, decoded.Location.Longitude, 5);
    }

    [Theory]
    [InlineData(89.5, 0.0)]
    [InlineData(89.0, 0.0)]
    [InlineData(88.95, 0.0)]
    [InlineData(89.99, 11.0)]
    [InlineData(-89.5, 0.0)]
    [InlineData(0.0, -179.5)]
    [InlineData(0.0, -178.95)]
    [InlineData(45.0, -179.5)]
    [InlineData(-33.5, -179.5)]
    [InlineData(89.5, -179.5)]
    [InlineData(-89.5, -179.5)]
    public void CompressedPosition_ExtremeCoordinates_DoNotProduceGarbage(double lat, double lon)
    {
        // Regression: a decoder that branched on the magnitude of the base-91
        // value returned negative or wildly wrong coordinates at high latitudes
        // and far-west longitudes (where the encoded value is small).
        var packet = CreatePositionPacket(lat, lon, PositionFormat.Compressed);

        var infoField = AprsInfoFieldSerializer.FormatInfoField(packet);
        var decoded = Assert.IsType<PositionPacket>(
            AprsInfoFieldParser.ParseInfoField(infoField, packet.Source, packet.Destination));

        Assert.InRange(decoded.Location.Latitude, -90.0, 90.0);
        Assert.InRange(decoded.Location.Longitude, -180.0, 180.0);
        Assert.Equal(lat, decoded.Location.Latitude, 5);
        Assert.Equal(lon, decoded.Location.Longitude, 5);
    }

    // ---------------------------------------------------------------
    // Mic-E position tests
    // ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(MicECoordinates))]
    public void MicE_RoundTrip_PreservesCoordinates(double lat, double lon)
    {
        // Mic-E resolution is 0.01 minute (1.67e-4°), so a correct round trip is
        // accurate to about 8.3e-5°. A tolerance of 3 decimal places (5e-4°)
        // proves the encoder and decoder are inverses of each other.
        var packet = CreatePositionPacket(lat, lon, PositionFormat.MicE);

        var infoField = AprsInfoFieldSerializer.FormatInfoField(packet);
        var destination = MicESerializer.EncodeDestination(packet);

        var decoded = Assert.IsType<PositionPacket>(
            AprsInfoFieldParser.ParseInfoField(infoField, packet.Source, destination));

        Assert.Equal(lat, decoded.Location.Latitude, 3);
        Assert.Equal(lon, decoded.Location.Longitude, 3);
    }

    [Fact]
    public void MicE_NorthEast_DecodesCorrectly()
    {
        // 40°30.30'N, 45°30.00'E
        var info = "`I:\x1C   />";
        var result = AprsInfoFieldParser.ParseInfoField(
            info,
            new Callsign("TEST"),
            new Callsign("403030"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(40.505, pos.Location.Latitude, 3);
        Assert.Equal(45.5, pos.Location.Longitude, 3);
    }

    [Fact]
    public void MicE_NorthWest_DecodesCorrectly()
    {
        // 40°30.00'N, 74°00.00'W
        var info = "`f\x1C\x1C   />";
        var result = AprsInfoFieldParser.ParseInfoField(
            info,
            new Callsign("TEST"),
            new Callsign("40300P"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(40.5, pos.Location.Latitude, 3);
        Assert.Equal(-74.0, pos.Location.Longitude, 3);
    }

    [Fact]
    public void MicE_SouthEast_DecodesCorrectly()
    {
        // 33°30.00'S, 151°12.00'E (longitude offset of +100 in effect)
        var info = "`O(\x1C   />";
        var result = AprsInfoFieldParser.ParseInfoField(
            info,
            new Callsign("TEST"),
            new Callsign("SSSPP0"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(-33.5, pos.Location.Latitude, 3);
        Assert.Equal(151.2, pos.Location.Longitude, 3);
    }

    [Fact]
    public void MicE_SouthWest_DecodesCorrectly()
    {
        // 23°30.00'S, 46°36.00'W
        var info = "`J@\x1C   />";
        var result = AprsInfoFieldParser.ParseInfoField(
            info,
            new Callsign("TEST"),
            new Callsign("RSSP0P"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(-23.5, pos.Location.Latitude, 3);
        Assert.Equal(-46.6, pos.Location.Longitude, 3);
    }

    [Fact]
    public void MicE_Equator_DecodesCorrectly()
    {
        // 0°00.00'N, 0°00.00'E
        var info = "`\x1C\x1C\x1C   />";
        var result = AprsInfoFieldParser.ParseInfoField(
            info,
            new Callsign("TEST"),
            new Callsign("000000"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(0.0, pos.Location.Latitude, 3);
        Assert.Equal(0.0, pos.Location.Longitude, 3);
    }

    [Fact]
    public void MicE_MaximumLatitude_North_DecodesCorrectly()
    {
        // 89°59.99'N
        var info = "`\x1C\x1C\x1C   />";
        var result = AprsInfoFieldParser.ParseInfoField(
            info,
            new Callsign("TEST"),
            new Callsign("895999"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(89.9998, pos.Location.Latitude, 3);
        Assert.InRange(pos.Location.Longitude, -180, 180);
    }

    [Fact]
    public void MicE_MaximumLatitude_South_DecodesCorrectly()
    {
        // 89°59.99'S — build the destination with the encoder so the flag bits
        // are placed per the Mic-E spec (index 3 >= 'P' marks south).
        var packet = CreatePositionPacket(-89.9998, 0.0, PositionFormat.MicE);
        var destination = MicESerializer.EncodeDestination(packet);

        var result = AprsInfoFieldParser.ParseInfoField(
            "`\x1C\x1C\x1C   />",
            new Callsign("TEST"),
            destination);

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(-89.9998, pos.Location.Latitude, 3);
    }

    private static PositionPacket CreatePositionPacket(double lat, double lon, PositionFormat format)
    {
        return new PositionPacket
        {
            Source = new Callsign("TEST"),
            Destination = new Callsign("APZ001"),
            Path = System.Array.Empty<Callsign>(),
            Location = new Geo.Coordinate(lat, lon),
            Symbol = new Symbol(SymbolTable.Primary, 'u'.ToSymbolCode(), null),
            Format = format,
        };
    }
}

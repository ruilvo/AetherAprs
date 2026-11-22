// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

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
    // ---------------------------------------------------------------
    // Compressed position tests - APRS spec format (small values)
    // ---------------------------------------------------------------

    [Fact(Skip = "TODO: Generate known-good test vectors from reference implementation")]
    public void CompressedPosition_SpecFormat_NorthEast_DecodesCorrectly()
    {
        // Test coordinates: 40.0°N, 10.0°E (Rome, Italy area)
        // Using APRS spec formula: latN = 380926 × (90 - 40) / 180 = 105813
        // lonN = 190463 × (180 + 10) / 360 = 100465
        // These are small values < threshold, so spec formula should be used
        
        // This test documents the expected behavior.
        // The actual encoded string needs to be verified against reference implementation.
    }

    [Fact]
    public void CompressedPosition_AprsLibFormat_NorthWest_DecodesCorrectly()
    {
        // Known good test from existing tests - uses aprslib format (large values)
        // Coordinates: 42.2419°N, 8.59665°W
        var result = AprsInfoFieldParser.ParseInfoField("!/9-pFL>:4uBkQ",
            new Callsign("N0CALL"), new Callsign("APZ001"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(42.2419, pos.Location.Latitude, 4);
        Assert.Equal(-8.59665, pos.Location.Longitude, 5);
    }

    [Theory]
    [InlineData(90.0, 0.0)]     // North pole
    [InlineData(-90.0, 0.0)]    // South pole
    [InlineData(0.0, 180.0)]    // International date line (East)
    [InlineData(0.0, -180.0)]   // International date line (West)
    [InlineData(0.0, 0.0)]      // Null Island
    [InlineData(45.0, 45.0)]    // NE quadrant
    [InlineData(45.0, -45.0)]   // NW quadrant
    [InlineData(-45.0, 45.0)]   // SE quadrant
    [InlineData(-45.0, -45.0)]  // SW quadrant
    public void CompressedPosition_RoundTrip_PreservesCoordinates(double lat, double lon)
    {
        // This test verifies that encoding and then decoding preserves coordinates
        // within the precision limits of the compressed format
        
        // Create a position packet
        var packet = new PositionPacket
        {
            Source = new Callsign("TEST"),
            Destination = new Callsign("APZ001"),
            Path = System.Array.Empty<Callsign>(),
            Location = new Geo.Coordinate(lat, lon),
            Symbol = new Symbol(SymbolTable.Primary, 'u'.ToSymbolCode(), null),
            Format = PositionFormat.Compressed
        };

        // Serialize to compressed format
        var infoField = AprsInfoFieldSerializer.FormatInfoField(packet);

        // Parse it back
        var result = AprsInfoFieldParser.ParseInfoField(infoField,
            packet.Source, packet.Destination);

        var decoded = Assert.IsType<PositionPacket>(result);
        
        // Compressed format has limited precision (~3-4 decimal places for lat/lon)
        // Allow tolerance of 0.01 degrees (~1 km)
        Assert.Equal(lat, decoded.Location.Latitude, 2);
        Assert.Equal(lon, decoded.Location.Longitude, 2);
    }

    // ---------------------------------------------------------------
    // Mic-E position tests - all quadrants
    // ---------------------------------------------------------------

    [Fact]
    public void MicE_NorthEast_DecodesCorrectly()
    {
        // Test coordinates: 40.5°N, 45.5°E (Azerbaijan area)
        // Destination: 4030.30N,04530.00E
        // Digits: 4, 0, 3, 0, 3, 0 (DDMM.HH format)
        // Characters encoding:
        // - Digit 0-9: can be '0'-'9' (North), 'P'-'Y' (South), or 'A'-'J' (North)
        // - Position 4 (index 3): '0'-'L' = North, 'P'-'Z' = South
        // - Position 5 (index 4): '0'-'L' = lon offset 0, 'P'-'Y' = lon offset 100
        // - Position 6 (index 5): '0'-'L' = East, 'P'-'Z' = West
        
        // For 40°30.30'N: Use '4', '0', '3', '0' (North), '3', '0' (East, no offset)
        // Destination: "403030" with proper encoding
        // Character 4 (index 3) should be '0' (< 'L' = North)
        // Character 5 (index 4) should be 'P' or higher for +100 offset OR '0'-'L' for 0
        // Character 6 (index 5) should be '0' (< 'P' = East)
        
        // Let's use: "4" + "0" + "3" + "0" + "3" + "0"
        // But we need to encode with proper Mic-E characters
        // For North + East + offset 100: use P-Y for position 5
        
        // Simplified: Use destination "403P30" where:
        // - '4', '0', '3' encode digits 4, 0, 3
        // - 'P' at position 4 (index 3) encodes digit 0 AND marks south? No wait...
        
        // Let me clarify the Mic-E encoding rules:
        // Position 1-3: Latitude degrees and minutes
        // Position 4: Latitude minutes tens (also N/S bit)
        // Position 5: Latitude hundredths tens (also lon offset bit)
        // Position 6: Latitude hundredths ones (also E/W bit)
        
        // For 40°30.30'N, 45°30.00'E:
        // Lat: 40 degrees, 30 minutes, 30 hundredths
        // Lon: 45 degrees, 30 minutes, 00 hundredths (no offset needed since < 100)
        
        // Digits: [4, 0, 3, 0, 3, 0]
        // Char 0: '4' (digit 4)
        // Char 1: '0' (digit 0)  
        // Char 2: '3' (digit 3)
        // Char 3: '0' (digit 0, North because '0' <= 'L')
        // Char 4: '3' (digit 3, offset 0 because '3' < 'P')
        // Char 5: '0' (digit 0, East because '0' < 'P')
        
        // Destination: "403030"
        
        // Longitude encoding in info field:
        // 45 degrees, 30 minutes, 00 hundredths
        // lonDeg byte: 45 + 28 + offset = 45 + 28 + 0 = 73 = 'I'
        // lonMin byte: 30 + 28 = 58 = ':'
        // lonHundredths byte: 0 + 28 = 28 = '\x1C'
        
        // Info field: '`' + "I:\x1C" + speed/course(3) + symbol(2)
        // Using dummy speed/course and symbol: "   />"
        var info = "`I:\x1C   />";
        
        var result = AprsInfoFieldParser.ParseInfoField(info,
            new Callsign("TEST"), new Callsign("403030"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(40.505, pos.Location.Latitude, 3); // 40°30.30' = 40.505°
        Assert.Equal(45.5, pos.Location.Longitude, 3);   // 45°30.00' = 45.5°
    }

    [Fact]
    public void MicE_NorthWest_DecodesCorrectly()
    {
        // Test coordinates: 40.5°N, 74.0°W (New York area)
        // Destination: 4030.00N,07400.00W
        // Digits: [4, 0, 3, 0, 0, 0]
        // Char 0: '4' (digit 4)
        // Char 1: '0' (digit 0)
        // Char 2: '3' (digit 3)
        // Char 3: '0' (digit 0, North)
        // Char 4: '0' (digit 0, offset 0)
        // Char 5: 'P' (digit 0, West because >= 'P')
        
        // Destination: "40300P"
        
        // Longitude: 74 degrees, 0 minutes, 0 hundredths
        // lonDeg: 74 + 28 + 0 = 102 = 'f'
        // lonMin: 0 + 28 = 28 = '\x1C'
        // lonHundredths: 0 + 28 = 28 = '\x1C'
        
        var info = "`f\x1C\x1C   />";
        
        var result = AprsInfoFieldParser.ParseInfoField(info,
            new Callsign("TEST"), new Callsign("40300P"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(40.5, pos.Location.Latitude, 3);
        Assert.Equal(-74.0, pos.Location.Longitude, 3);
    }

    [Fact]
    public void MicE_SouthEast_DecodesCorrectly()
    {
        // Test coordinates: 33.5°S, 151.2°E (Sydney area)
        // Destination: 3330.00S,15112.00E
        // Digits: [3, 3, 3, 0, 0, 0]
        // Need longitude offset of +100 since lon > 100
        // Char 0: 'S' (digit 3, South because 'S' > 'L')
        // Char 1: 'S' (digit 3, South)
        // Char 2: 'S' (digit 3, South)
        // Char 3: 'P' (digit 0, South because 'P' > 'L')
        // Char 4: 'P' (digit 0, offset 100 because 'P' >= 'P')
        // Char 5: '0' (digit 0, East because '0' < 'P')
        
        // Destination: "SSSPP0"
        
        // Longitude: 151 degrees, 12 minutes, 0 hundredths
        // With offset 100: encoded as 151 - 100 = 51
        // lonDeg: 51 + 28 + 100 = 179, but need adjustment
        // Actually, lonDeg after offset: (51 + 28 + 100) may need the 180-189 adjustment
        // Let me recalculate: lonDeg calculation uses offset from char 4
        // lonDeg = info[pos] - 28 + offset where offset = 100
        // To encode 151: need info[pos] = 151 - offset + 28 = 151 - 100 + 28 = 79 = 'O'
        // lonMin: 12 + 28 = 40 = '('
        // lonHundredths: 0 + 28 = 28 = '\x1C'
        
        var info = "`O(\x1C   />";
        
        var result = AprsInfoFieldParser.ParseInfoField(info,
            new Callsign("TEST"), new Callsign("SSSPP0"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(-33.5, pos.Location.Latitude, 3);
        Assert.Equal(151.2, pos.Location.Longitude, 3);
    }

    [Fact]
    public void MicE_SouthWest_DecodesCorrectly()
    {
        // Test coordinates: 23.5°S, 46.6°W (São Paulo area)
        // Destination: 2330.00S,04636.00W
        // Digits: [2, 3, 3, 0, 0, 0]
        // Char 0: 'R' (digit 2, South)
        // Char 1: 'S' (digit 3, South)
        // Char 2: 'S' (digit 3, South)
        // Char 3: 'P' (digit 0, South)
        // Char 4: '0' (digit 0, offset 0)
        // Char 5: 'P' (digit 0, West)
        
        // Destination: "RSSP0P"
        
        // Longitude: 46 degrees, 36 minutes, 0 hundredths
        // lonDeg: 46 + 28 + 0 = 74 = 'J'
        // lonMin: 36 + 28 = 64, but if >= 60, subtract 60: 64 - 60 = 4 + 28 = 32 = ' '
        // Wait, the encoding adds 28 first, then the decoder checks if >= 60 after subtracting 28
        // So to encode 36: we want decoder to get 36
        // Decoder: lonMin = info[pos+1] - 28, if >= 60 then -= 60
        // To get 36: if we encode as 36 + 28 = 64, decoder gets 64 - 28 = 36 (no adjustment)
        // lonMin: 36 + 28 = 64 = '@'
        // lonHundredths: 0 + 28 = 28 = '\x1C'
        
        var info = "`J@\x1C   />";
        
        var result = AprsInfoFieldParser.ParseInfoField(info,
            new Callsign("TEST"), new Callsign("RSSP0P"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(-23.5, pos.Location.Latitude, 3);
        Assert.Equal(-46.6, pos.Location.Longitude, 3);
    }

    [Fact]
    public void MicE_LatitudeAtEquator_DecodesCorrectly()
    {
        // Test coordinates: 0.0°N, 0.0°E (Gulf of Guinea)
        // Destination: 0000.00N,00000.00E
        // Digits: [0, 0, 0, 0, 0, 0]
        // All zeros, North, East, no offset
        
        // Destination: "000000"
        
        // Longitude: 0 degrees, 0 minutes, 0 hundredths
        // lonDeg: 0 + 28 + 0 = 28 = '\x1C'
        // lonMin: 0 + 28 = 28 = '\x1C'
        // lonHundredths: 0 + 28 = 28 = '\x1C'
        
        var info = "`\x1C\x1C\x1C   />";
        
        var result = AprsInfoFieldParser.ParseInfoField(info,
            new Callsign("TEST"), new Callsign("000000"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(0.0, pos.Location.Latitude, 3);
        Assert.Equal(0.0, pos.Location.Longitude, 3);
    }

    [Fact]
    public void MicE_MaximumLatitude_North_DecodesCorrectly()
    {
        // Test coordinates: 89°59.99'N (near North Pole)
        // Destination: 8959.99N
        // Digits: [8, 9, 5, 9, 9, 9]
        
        // Destination: "895999"
        
        // With arbitrary longitude 0
        var info = "`\x1C\x1C\x1C   />";
        
        var result = AprsInfoFieldParser.ParseInfoField(info,
            new Callsign("TEST"), new Callsign("895999"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(89.9998, pos.Location.Latitude, 3); // 89°59.99' ≈ 89.9998°
        Assert.InRange(pos.Location.Longitude, -180, 180);
    }
}

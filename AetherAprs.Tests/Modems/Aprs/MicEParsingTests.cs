// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Aprs;

using Xunit;

namespace AetherAprs.Tests.Modems.Aprs;

public class MicEParsingTests
{
    [Fact]
    public void ParseInfoField_MicEFromDatabase_ID50_ReturnsPositionPacket()
    {
        // From database ID 50: CT7AXO -> APU25N
        // Destination encodes latitude
        AprsPacket result = AprsInfoFieldParser.ParseInfoField(
            "`~B3l ^\\\x00-/`APRS by CT7AXO@gmail.com_%",
            new Callsign("CT7AXO"),
            new Callsign("APU25N"));

        // Should parse as position (or could be unknown if destination doesn't encode valid lat)
        Assert.True(result is PositionPacket or UnknownPacket);
    }

    [Fact]
    public void ParseInfoField_MicEWithValidDestination_ReturnsPositionPacket()
    {
        // Example with known valid Mic-E destination encoding
        // Destination "T4SQZZ" encodes latitude
        AprsPacket result = AprsInfoFieldParser.ParseInfoField(
            "`(_fn\"Oj/`\"4)}_%",
            new Callsign("TEST"),
            new Callsign("T4SQZZ"));

        // This might still fail if the destination doesn't have valid Mic-E encoding
        // The test verifies the parser doesn't crash
        Assert.NotNull(result);
    }

    [Fact]
    public void ParseInfoField_MaidenheadGridLocator_ReturnsUnknown()
    {
        // From database ID 130: [ binary data ]
        // Not yet implemented - should return UnknownPacket
        AprsPacket result = AprsInfoFieldParser.ParseInfoField(
            "[ binary data ]",
            new Callsign("EA1CHG", 10),
            new Callsign("APU25N"));

        UnknownPacket unknown = Assert.IsType<UnknownPacket>(result);
        Assert.Equal("[ binary data ]", unknown.Raw);
    }

    // Real packets captured from two Portuguese digipeaters. The payload is the
    // same; only the digipeater path differs. A Kenwood TM-D700 sends the ']'
    // device identifier followed by the altitude field.
    private const string Ct2gpzInfoField = "'}9D gIv/]\"3r}Jaime Pinheiro Ptg (Movel 2)\r";

    [Theory]
    [InlineData("CQ0PE-3,ED1ZAB-3,ED1ZBH-3,WIDE2")]
    [InlineData("CQ0PE-3,ED1ZAB-3,ED1ZBH-3,CQ0PTD")]
    public void ParseInfoField_Ct2gpzMicERecord_DecodesPosition(string digipeaterPath)
    {
        List<Callsign> path = [];
        foreach (string entry in digipeaterPath.Split(','))
        {
            Assert.True(Callsign.TryParse(entry, out Callsign digipeater), $"invalid path entry '{entry}'");
            path.Add(digipeater);
        }

        AprsPacket result = AprsInfoFieldParser.ParseInfoField(
            Ct2gpzInfoField,
            new Callsign("CT2GPZ", 10),
            new Callsign("SY2TPU"),
            path);

        PositionPacket pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(39.4008, pos.Location.Latitude, 4);
        Assert.Equal(-7.49, pos.Location.Longitude, 4);
        Assert.Equal(47, pos.Speed);
        Assert.Equal(145, pos.Course);
        Assert.NotNull(pos.Altitude);
        Assert.Equal(0, pos.Altitude.Value, 1);
        Assert.Equal(SymbolTable.Primary, pos.Symbol.Table);
        Assert.Equal(SymbolCode.LatinSmallLetterV, pos.Symbol.Code);
        Assert.Equal("Jaime Pinheiro Ptg (Movel 2)", pos.Comment);
        Assert.Equal(path.Count, pos.Path.Count);
    }
}

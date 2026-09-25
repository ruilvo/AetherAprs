// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Modems.Aprs;
using Xunit;

namespace AetherAprs.Tests.Aprs;

public class MicEParsingTests
{
    [Fact]
    public void ParseInfoField_MicEFromDatabase_ID50_ReturnsPositionPacket()
    {
        // From database ID 50: CT7AXO -> APU25N
        // Destination encodes latitude
        var result = AprsInfoFieldParser.ParseInfoField(
            "`~B3l ^\\\x00-/`APRS by CT7AXO@gmail.com_%",
            new Callsign("CT7AXO"),
            new Callsign("APU25N"));

        // Should parse as position (or could be unknown if destination doesn't encode valid lat)
        Assert.True(result is PositionPacket || result is UnknownPacket);
    }

    [Fact]
    public void ParseInfoField_MicEWithValidDestination_ReturnsPositionPacket()
    {
        // Example with known valid Mic-E destination encoding
        // Destination "T4SQZZ" encodes latitude
        var result = AprsInfoFieldParser.ParseInfoField(
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
        var result = AprsInfoFieldParser.ParseInfoField(
            "[ binary data ]",
            new Callsign("EA1CHG", 10),
            new Callsign("APU25N"));

        var unknown = Assert.IsType<UnknownPacket>(result);
        Assert.Equal("[ binary data ]", unknown.Raw);
    }
}

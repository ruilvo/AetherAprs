// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Modems.Aprs;
using Xunit;

namespace AetherAprs.Tests.Aprs;

public class OverlayParsingTests
{
    [Fact]
    public void ParseInfoField_PositionWithNumericOverlay_D_ReturnsPositionPacket()
    {
        // From database ID 1: !4038.72ND00747.95W&
        // Latitude: 40°38.72'N = 40.6453°N
        // Overlay: D
        // Longitude: 007°47.95'W = 7.7992°W
        // Symbol: &
        var result = AprsInfoFieldParser.ParseInfoField(
            "!4038.72ND00747.95W&RNG0001 440 Voice 433.45000MHz +0.0000MHz",
            new Callsign("APRS"), 
            new Callsign("APDG02"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(40.6453, pos.Latitude, 4);
        Assert.Equal(-7.7992, pos.Longitude, 4);
        Assert.Equal(SymbolTable.Alternate, pos.Symbol.Table);
        Assert.Equal('D', pos.Symbol.Overlay);
        Assert.Equal(SymbolCode.Ampersand, pos.Symbol.Code);
    }

    [Fact]
    public void ParseInfoField_PositionWithNumericOverlay_W_ReturnsPositionPacket()
    {
        // From database ID 32: !4053.58NW00826.71Wi
        // Latitude: 40°53.58'N = 40.893°N
        // Overlay: W
        // Longitude: 008°26.71'W = 8.4452°W
        // Symbol: i
        var result = AprsInfoFieldParser.ParseInfoField(
            "!4053.58NW00826.71Wi/A=00000070cm MMDVM Voice (DMR)",
            new Callsign("APRS"), 
            new Callsign("APCHP0"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(40.893, pos.Latitude, 3);
        Assert.Equal(-8.4452, pos.Longitude, 4);
        Assert.Equal(SymbolTable.Alternate, pos.Symbol.Table);
        Assert.Equal('W', pos.Symbol.Overlay);
        Assert.Equal(SymbolCode.LatinSmallLetterI, pos.Symbol.Code);
    }

    [Fact]
    public void ParseInfoField_PositionWithBackslashSeparator_ReturnsPositionPacket()
    {
        // From database ID 4: !4013.00N\00824.78Wj
        // This should work correctly (baseline test)
        var result = AprsInfoFieldParser.ParseInfoField(
            "!4013.00N\\00824.78Wj#Obras-(Metro Mondego)",
            new Callsign("APRS"), 
            new Callsign("APHPIB"));

        var pos = Assert.IsType<PositionPacket>(result);
        Assert.Equal(40.2167, pos.Latitude, 4);
        Assert.Equal(-8.4130, pos.Longitude, 4);
        Assert.Equal(SymbolTable.Alternate, pos.Symbol.Table);
        Assert.Null(pos.Symbol.Overlay);
        Assert.Equal(SymbolCode.LatinSmallLetterJ, pos.Symbol.Code);
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Reflection;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using Xunit;

namespace AetherAprs.Tests.Imaging;

public sealed class AprsSymbolBitmapProviderTests
{
    [Theory]
    [InlineData('!', 0, 0)]
    [InlineData('/', 0, 14)]
    [InlineData('0', 0, 15)]
    [InlineData('9', 1, 8)]
    [InlineData(':', 1, 9)]
    public void PackedSpriteSheetMapsAsciiCodeToExpectedCell(char code, int expectedRow, int expectedColumn)
    {
        // GetCellPosition is now internal, accessible via InternalsVisibleTo
        var symbolCode = code.ToSymbolCode();
        var result = AprsSymbolBitmapProvider.GetCellPosition(symbolCode);

        Assert.Equal(expectedRow, result.row);
        Assert.Equal(expectedColumn, result.col);
    }

    [Fact]
    public void PackedSpriteSheetRejectsSpaceBecauseItHasNoCell()
    {
        // GetCellPosition is now internal, accessible via InternalsVisibleTo
        var symbolCode = SymbolCode.Space;
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => AprsSymbolBitmapProvider.GetCellPosition(symbolCode));

        Assert.NotNull(exception);
    }

    [Fact]
    public void GetCellPositionCalculatesCorrectPositionForOverlayCharacters()
    {
        // Test that overlay characters (like '0'-'9', 'A'-'Z') map to correct cells
        // GetCellPosition is now internal, accessible via InternalsVisibleTo

        // Test digit '5' which should map to a specific cell
        var symbolCode = '5'.ToSymbolCode();
        var result = AprsSymbolBitmapProvider.GetCellPosition(symbolCode);
        
        // '5' is ASCII 0x35 (53), offset by 0x21 (33) = index 20
        // index 20 / 16 columns = row 1, 20 % 16 = column 4
        Assert.Equal(1, result.row);
        Assert.Equal(4, result.col);
    }

    [Fact]
    public void GetCellPositionCalculatesCorrectPositionForLetterOverlays()
    {
        // GetCellPosition is now internal, accessible via InternalsVisibleTo

        // Test 'A' which is a common overlay character
        var symbolCode = 'A'.ToSymbolCode();
        var result = AprsSymbolBitmapProvider.GetCellPosition(symbolCode);
        
        // 'A' is ASCII 0x41 (65), offset by 0x21 (33) = index 32
        // index 32 / 16 columns = row 2, 32 % 16 = column 0
        Assert.Equal(2, result.row);
        Assert.Equal(0, result.col);
    }
}

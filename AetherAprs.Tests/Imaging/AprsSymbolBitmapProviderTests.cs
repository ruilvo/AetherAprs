// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Reflection;
using AetherAprs.Imaging;
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
        var method = typeof(AprsSymbolBitmapProvider).GetMethod(
            "GetCellPosition",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = ((int row, int col))method!.Invoke(null, [(int)code])!;

        Assert.Equal(expectedRow, result.row);
        Assert.Equal(expectedColumn, result.col);
    }

    [Fact]
    public void PackedSpriteSheetRejectsSpaceBecauseItHasNoCell()
    {
        var method = typeof(AprsSymbolBitmapProvider).GetMethod(
            "GetCellPosition",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var exception = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, [' ']));

        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }
}

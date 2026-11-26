// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using System;
using Xunit;

namespace AetherAprs.Tests.Models.Aprs;

/// <summary>
/// Object, item and bulletin names share one fixed-width field, so a name that is too
/// wide would desynchronize every field that follows it.
/// </summary>
public sealed class AprsNameFieldTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("APRS2SOTA")]
    [InlineData("WX STN 1")]
    public void IsValid_WhenTheNameFitsTheField_IsTrue(string name) =>
        Assert.True(AprsNameField.IsValid(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TOOLONGNAME")]
    [InlineData("BAD\tNAME")]
    public void IsValid_WhenTheNameDoesNotFitTheField_IsFalse(string? name) =>
        Assert.False(AprsNameField.IsValid(name));

    [Fact]
    public void MaximumLength_IsTheNineCharacterField()
    {
        Assert.Equal(9, AprsNameField.MaximumLength);
        Assert.True(AprsNameField.IsValid(new string('A', AprsNameField.MaximumLength)));
        Assert.False(AprsNameField.IsValid(new string('A', AprsNameField.MaximumLength + 1)));
    }

    [Fact]
    public void EnsureValid_WhenTheNameIsTooWide_Throws()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => AprsNameField.EnsureValid("TOOLONGNAME", "name"));

        Assert.Equal("name", exception.ParamName);
        Assert.Contains("1-9", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureValid_WhenTheNameFits_DoesNotThrow() =>
        AprsNameField.EnsureValid("APRS2SOTA", "name");
}

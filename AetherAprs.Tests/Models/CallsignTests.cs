// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models.Aprs;
using Xunit;

namespace AetherAprs.Tests.Models;

public class CallsignTests
{
    [Fact]
    public void Constructor_SimpleCallsign_SetsBase()
    {
        Callsign call = new("N0CALL");
        Assert.Equal("N0CALL", call.Base);
        Assert.Null(call.Ssid);
    }

    [Fact]
    public void Constructor_CallsignWithSsid_SetsBaseAndSsid()
    {
        Callsign call = new("N0CALL", 5);
        Assert.Equal("N0CALL", call.Base);
        Assert.Equal(5, call.Ssid);
    }

    [Fact]
    public void Constructor_ConvertsToUpper()
    {
        Callsign call = new("n0call");
        Assert.Equal("N0CALL", call.Base);
    }

    [Fact]
    public void Constructor_TrimsWhitespace()
    {
        Callsign call = new("  N0CALL  ");
        Assert.Equal("N0CALL", call.Base);
    }

    [Fact]
    public void Constructor_NullBase_ThrowsArgumentNullException() => Assert.Throws<ArgumentNullException>(() => new Callsign(null!));

    [Fact]
    public void Constructor_EmptyBase_ThrowsArgumentException() => Assert.Throws<ArgumentException>(() => new Callsign(""));

    [Fact]
    public void Constructor_BaseTooShort_ThrowsArgumentException() => Assert.Throws<ArgumentException>(() => new Callsign("A"));

    [Fact]
    public void Constructor_BaseTooLong_ThrowsArgumentException() => Assert.Throws<ArgumentException>(() => new Callsign("TOOLONG"));

    [Theory]
    [InlineData("N0 CALL")]
    [InlineData("N0@ALL")]
    [InlineData("N0CÅLL")]
    public void Constructor_InvalidCharacters_ThrowsArgumentException(string value) => Assert.Throws<ArgumentException>(() => new Callsign(value));

    [Fact]
    public void Constructor_SsidTooLow_ThrowsArgumentOutOfRangeException() => Assert.Throws<ArgumentOutOfRangeException>(() => new Callsign("N0CALL", -1));

    [Fact]
    public void Constructor_SsidTooHigh_ThrowsArgumentOutOfRangeException() => Assert.Throws<ArgumentOutOfRangeException>(() => new Callsign("N0CALL", 16));

    [Fact]
    public void ToString_WithoutSsid_ReturnsBase()
    {
        Callsign call = new("N0CALL");
        Assert.Equal("N0CALL", call.ToString());
    }

    [Fact]
    public void ToString_WithSsid_ReturnsBaseDashSsid()
    {
        Callsign call = new("N0CALL", 5);
        Assert.Equal("N0CALL-5", call.ToString());
    }

    [Fact]
    public void ToString_WithZeroSsid_ReturnsBase()
    {
        Callsign call = new("N0CALL", 0);

        Assert.Equal("N0CALL", call.ToString());
    }

    [Fact]
    public void Equality_SameCallsign_AreEqual()
    {
        Callsign a = new("N0CALL", 1);
        Callsign b = new("N0CALL", 1);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentSsid_AreNotEqual()
    {
        Callsign a = new("N0CALL", 1);
        Callsign b = new("N0CALL", 2);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_DifferentBase_AreNotEqual()
    {
        Callsign a = new("N0CALL");
        Callsign b = new("N1CALL");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_ZeroSsidAndNoSsid_AreNotEqual()
    {
        // Both print as "N0CALL", but SSID 0 carries AX.25 meaning (for example a
        // repeated path element). Never use ToString() as an identity key.
        Callsign withZeroSsid = new("N0CALL", 0);
        Callsign withoutSsid = new("N0CALL");

        Assert.NotEqual(withZeroSsid, withoutSsid);
        Assert.Equal(withZeroSsid.ToString(), withoutSsid.ToString());
    }

    [Fact]
    public void TryParse_ExplicitZeroSsid_PreservesZero()
    {
        Assert.True(Callsign.TryParse("N0CALL-0", out Callsign callsign));

        Assert.Equal(0, callsign.Ssid);
    }

    [Theory]
    [InlineData("N0CALL", "N0CALL", null)]
    [InlineData("n0call-1", "N0CALL", 1)]
    [InlineData("  K0ABC-15 ", "K0ABC", 15)]
    public void TryParse_ValidCallsign_ReturnsTrue(string input, string expectedBase, int? expectedSsid)
    {
        Assert.True(Callsign.TryParse(input, out Callsign callsign));
        Assert.Equal(expectedBase, callsign.Base);
        Assert.Equal(expectedSsid, callsign.Ssid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData("TOOLONG")]
    [InlineData("N0CALL-99")]
    [InlineData("N0CALL-X")]
    public void TryParse_InvalidCallsign_ReturnsFalse(string? input) => Assert.False(Callsign.TryParse(input, out _));
}

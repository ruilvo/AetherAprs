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
        var call = new Callsign("N0CALL");
        Assert.Equal("N0CALL", call.Base);
        Assert.Null(call.Ssid);
    }

    [Fact]
    public void Constructor_CallsignWithSsid_SetsBaseAndSsid()
    {
        var call = new Callsign("N0CALL", 5);
        Assert.Equal("N0CALL", call.Base);
        Assert.Equal(5, call.Ssid);
    }

    [Fact]
    public void Constructor_ConvertsToUpper()
    {
        var call = new Callsign("n0call");
        Assert.Equal("N0CALL", call.Base);
    }

    [Fact]
    public void Constructor_TrimsWhitespace()
    {
        var call = new Callsign("  N0CALL  ");
        Assert.Equal("N0CALL", call.Base);
    }

    [Fact]
    public void Constructor_NullBase_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new Callsign(null!));
    }

    [Fact]
    public void Constructor_EmptyBase_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Callsign(""));
    }

    [Fact]
    public void Constructor_BaseTooShort_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Callsign("A"));
    }

    [Fact]
    public void Constructor_BaseTooLong_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Callsign("TOOLONG"));
    }

    [Fact]
    public void Constructor_SsidTooLow_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Callsign("N0CALL", -1));
    }

    [Fact]
    public void Constructor_SsidTooHigh_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Callsign("N0CALL", 16));
    }

    [Fact]
    public void ToString_WithoutSsid_ReturnsBase()
    {
        var call = new Callsign("N0CALL");
        Assert.Equal("N0CALL", call.ToString());
    }

    [Fact]
    public void ToString_WithSsid_ReturnsBaseDashSsid()
    {
        var call = new Callsign("N0CALL", 5);
        Assert.Equal("N0CALL-5", call.ToString());
    }

    [Fact]
    public void Equality_SameCallsign_AreEqual()
    {
        var a = new Callsign("N0CALL", 1);
        var b = new Callsign("N0CALL", 1);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentSsid_AreNotEqual()
    {
        var a = new Callsign("N0CALL", 1);
        var b = new Callsign("N0CALL", 2);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_DifferentBase_AreNotEqual()
    {
        var a = new Callsign("N0CALL");
        var b = new Callsign("N1CALL");
        Assert.NotEqual(a, b);
    }
}
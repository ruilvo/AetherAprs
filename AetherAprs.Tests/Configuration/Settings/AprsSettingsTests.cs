// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.Configuration.Settings;
using AetherAprs.Models.Aprs;

using Xunit;

namespace AetherAprs.Tests.Configuration.Settings;

public sealed class AprsSettingsTests
{
    [Fact]
    public void Callsign_Empty_IsRejected()
    {
        AprsSettings settings = new();

        Assert.Throws<ArgumentException>(() =>
        {
            _ = settings with { Callsign = string.Empty };
        });
    }

    [Theory]
    [InlineData("TOOLONG7")]
    [InlineData("N0-CALL")]
    [InlineData("N0 CALL")]
    public void Callsign_Invalid_IsRejected(string callsign)
    {
        AprsSettings settings = new();

        Assert.Throws<ArgumentException>(() =>
        {
            _ = settings with { Callsign = callsign };
        });
    }

    [Fact]
    public void Callsign_IsUppercased()
    {
        AprsSettings settings = new();

        AprsSettings updated = settings with { Callsign = "ct7alw" };

        Assert.Equal("CT7ALW", updated.Callsign);
    }

    [Fact]
    public void Ssid_Undefined_IsRejected()
    {
        AprsSettings settings = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = settings with { Ssid = (AprsSsid)200 };
        });
    }

    [Fact]
    public void SymbolCode_Undefined_IsRejected()
    {
        AprsSettings settings = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = settings with { SymbolCode = (SymbolCode)1 };
        });
    }

    [Fact]
    public void SymbolOverlay_Undefined_IsRejected()
    {
        AprsSettings settings = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = settings with { SymbolOverlay = (SymbolCode)1 };
        });
    }

    [Fact]
    public void Update_LeavesTheOriginalSnapshotUnchanged()
    {
        AprsSettings settings = new();

        AprsSettings updated = settings with { Callsign = "CT7ALW" };

        Assert.Equal("N0CALL", settings.Callsign);
        Assert.Equal("CT7ALW", updated.Callsign);
    }

    [Fact]
    public void ForwardingGates_DefaultToOff()
    {
        AprsSettings settings = new();

        Assert.False(settings.EnableDigipeater);
        Assert.False(settings.EnableAprsIsToRfGate);
        Assert.False(settings.EnableRfToAprsIsGate);
        Assert.Null(settings.AprsIsToRfAllowlist);
    }

    [Fact]
    public void ForwardingGates_KeepExplicitlySavedValues()
    {
        AprsSettings settings = new()
        {
            EnableDigipeater = true,
            EnableAprsIsToRfGate = true,
            EnableRfToAprsIsGate = true,
            AprsIsToRfAllowlist = "N0CALL",
        };

        Assert.True(settings.EnableDigipeater);
        Assert.True(settings.EnableAprsIsToRfGate);
        Assert.True(settings.EnableRfToAprsIsGate);
        Assert.Equal("N0CALL", settings.AprsIsToRfAllowlist);
    }
}

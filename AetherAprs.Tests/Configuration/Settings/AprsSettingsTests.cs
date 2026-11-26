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
        var settings = new AprsSettings();

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
        var settings = new AprsSettings();

        Assert.Throws<ArgumentException>(() =>
        {
            _ = settings with { Callsign = callsign };
        });
    }

    [Fact]
    public void Callsign_IsUppercased()
    {
        var settings = new AprsSettings();

        var updated = settings with { Callsign = "ct7alw" };

        Assert.Equal("CT7ALW", updated.Callsign);
    }

    [Fact]
    public void Ssid_Undefined_IsRejected()
    {
        var settings = new AprsSettings();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = settings with { Ssid = (AprsSsid)200 };
        });
    }

    [Fact]
    public void SymbolCode_Undefined_IsRejected()
    {
        var settings = new AprsSettings();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = settings with { SymbolCode = (SymbolCode)1 };
        });
    }

    [Fact]
    public void SymbolOverlay_Undefined_IsRejected()
    {
        var settings = new AprsSettings();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = settings with { SymbolOverlay = (SymbolCode)1 };
        });
    }

    [Fact]
    public void Update_LeavesTheOriginalSnapshotUnchanged()
    {
        var settings = new AprsSettings();

        var updated = settings with { Callsign = "CT7ALW" };

        Assert.Equal("N0CALL", settings.Callsign);
        Assert.Equal("CT7ALW", updated.Callsign);
    }
}

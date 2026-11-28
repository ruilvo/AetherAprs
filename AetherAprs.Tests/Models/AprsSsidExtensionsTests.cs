// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;

using Xunit;

namespace AetherAprs.Tests.Models;

public class AprsSsidExtensionsTests
{
    [Fact]
    public void FormatCallsign_NullSsid_ReturnsBaseCallsign()
    {
        AprsSsid? ssid = null;

        Assert.Equal("N0CALL", AprsSsidExtensions.FormatCallsign("N0CALL", ssid));
    }

    [Fact]
    public void FormatCallsign_PrimaryStation_ReturnsBaseCallsign()
    {
        AprsSsid? ssid = AprsSsid.PrimaryStation;

        Assert.Equal("N0CALL", AprsSsidExtensions.FormatCallsign("N0CALL", ssid));
    }

    [Fact]
    public void FormatCallsign_ZeroCastToSsid_ReturnsBaseCallsign() =>
        // SSIDs are 0-15, so the only zero value is PrimaryStation itself.
        Assert.Equal("N0CALL", AprsSsidExtensions.FormatCallsign("N0CALL", (AprsSsid)0));

    [Fact]
    public void FormatCallsign_NamedNonPrimarySsid_AppendsNumericSsid() => Assert.Equal("N0CALL-7", AprsSsidExtensions.FormatCallsign("N0CALL", AprsSsid.HumanPortable));

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    public void FormatCallsign_NonPrimarySsid_AppendsNumericSsid(byte ssidValue)
    {
        AprsSsid? ssid = (AprsSsid)ssidValue;

        Assert.Equal($"N0CALL-{ssidValue}", AprsSsidExtensions.FormatCallsign("N0CALL", ssid));
    }
}

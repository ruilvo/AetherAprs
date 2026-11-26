// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using Xunit;

namespace AetherAprs.Tests.Models.Aprs;

/// <summary>
/// An addressee is not a callsign: the APRS message field is nine characters wide and
/// carries tactical addresses as well as callsigns.
/// </summary>
public sealed class AprsAddressTests
{
    [Theory]
    [InlineData("APRS2SOTA")]
    [InlineData("EMAIL")]
    [InlineData("N0CALL")]
    [InlineData("N0CALL-15")]
    [InlineData("AB-1")]
    public void TryParse_WhenTheValueFitsAnAddressee_Parses(string value)
    {
        Assert.True(AprsAddress.TryParse(value, out AprsAddress address));
        Assert.False(string.IsNullOrWhiteSpace(address.Base));
    }

    [Fact]
    public void TryParse_WhenTheBaseIsNineCharacters_IsAccepted()
    {
        Assert.True(AprsAddress.TryParse("APRS2SOTA", out AprsAddress address));

        Assert.Equal("APRS2SOTA", address.Base);
        Assert.Null(address.Ssid);
        Assert.Equal(AprsAddress.MaximumBaseLength, address.Base.Length);
    }

    [Theory]
    [InlineData("TOOLONGADDRESS")]
    [InlineData("N0CALL-16")]
    [InlineData("APRS 2")]
    [InlineData("")]
    public void TryParse_WhenTheValueDoesNotFitAnAddressee_IsRejected(string value) => Assert.False(AprsAddress.TryParse(value, out _));

    [Fact]
    public void MaximumTextLength_AllowsAFullWidthBaseAndAnSsid()
    {
        Assert.Equal(9, AprsAddress.MaximumBaseLength);
        Assert.Equal(12, AprsAddress.MaximumTextLength);
        Assert.Equal(AprsAddress.MaximumTextLength, "APRS2SOTA-15".Length);
    }

    [Fact]
    public void FromStored_WhenTheStoredSsidIsZero_RestoresTheBaseOnlyAddress()
    {
        AprsAddress restored = AprsAddress.FromStored("APRS2SOTA", 0);

        Assert.Null(restored.Ssid);
        Assert.Equal(new AprsAddress("APRS2SOTA"), restored);
        Assert.NotEqual(new AprsAddress("APRS2SOTA", 0), restored);
    }

    [Fact]
    public void FromStored_WhenTheStoredSsidIsSet_KeepsIt()
    {
        AprsAddress restored = AprsAddress.FromStored("N0CALL", 7);

        Assert.Equal("N0CALL", restored.Base);
        Assert.Equal(7, restored.Ssid);
    }

    [Fact]
    public void FromCallsign_KeepsTheCallsignAndItsSsid()
    {
        AprsAddress address = AprsAddress.FromCallsign(new Callsign("N0CALL", 9));

        Assert.Equal("N0CALL", address.Base);
        Assert.Equal(9, address.Ssid);
    }
}

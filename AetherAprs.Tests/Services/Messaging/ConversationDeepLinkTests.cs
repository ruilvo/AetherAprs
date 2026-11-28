// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Services.Messaging;

using Xunit;

namespace AetherAprs.Tests.Services.Messaging;

/// <summary>
/// The conversation extra arrives from an exported activity, so these cases are
/// about rejecting input rather than about parsing valid addresses nicely.
/// </summary>
public sealed class ConversationDeepLinkTests
{
    [Theory]
    [InlineData("N0CALL")]
    [InlineData("n0call")]
    [InlineData("N0CALL-15")]
    [InlineData("APRS2SOTA")]
    [InlineData("aprs2sota-1")]
    [InlineData("EMAIL")]
    public void TryParsePeer_WhenTheValueIsAnAddressee_ReturnsThePeer(string value)
    {
        Assert.True(ConversationDeepLink.TryParsePeer(value, out AprsAddress peer));
        Assert.False(string.IsNullOrWhiteSpace(peer.Base));
    }

    [Fact]
    public void TryParsePeer_WhenTheValueIsATacticalAddress_KeepsTheNineCharacterBase()
    {
        Assert.True(ConversationDeepLink.TryParsePeer("APRS2SOTA", out AprsAddress peer));

        Assert.Equal("APRS2SOTA", peer.Base);
        Assert.Null(peer.Ssid);
    }

    [Fact]
    public void TryParsePeer_WhenTheValueIsACallsign_KeepsTheSsid()
    {
        Assert.True(ConversationDeepLink.TryParsePeer("N0CALL-15", out AprsAddress peer));

        Assert.Equal("N0CALL", peer.Base);
        Assert.Equal(15, peer.Ssid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" N0CALL")]
    [InlineData("N0CALL ")]
    [InlineData("N0CALL-16")]
    [InlineData("N0CALL>APRS")]
    [InlineData("N0CALL/../APRS")]
    [InlineData("N0CALL;rm -rf")]
    [InlineData("N0CALL\r\n")]
    [InlineData("TOOLONGADDRESS")]
    public void TryParsePeer_WhenTheValueIsNotAnAddressee_ReturnsNoPeer(string? value)
    {
        Assert.False(ConversationDeepLink.TryParsePeer(value, out AprsAddress peer));
        Assert.Equal(default, peer);
    }

    [Fact]
    public void TryParsePeer_WhenTheValueIsLongerThanAnAddressee_IsRejectedBeforeParsing()
    {
        string oversized = new string('N', ConversationDeepLink.MaximumLength + 1) + new string('0', 4096);

        Assert.False(ConversationDeepLink.TryParsePeer(oversized, out _));
    }

    [Fact]
    public void MaximumLength_MatchesTheLongestAddresseeText()
    {
        Assert.Equal(12, ConversationDeepLink.MaximumLength);
        Assert.Equal(AprsAddress.MaximumTextLength, ConversationDeepLink.MaximumLength);
        Assert.Equal(AprsAddress.MaximumTextLength, "APRS2SOTA-15".Length);
    }
}

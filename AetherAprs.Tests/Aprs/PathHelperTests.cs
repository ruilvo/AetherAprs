// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using System;
using Xunit;

namespace AetherAprs.Tests.Aprs;

public class PathHelperTests
{
    [Fact]
    public void ParsePath_EmptyString_ReturnsEmptyList()
    {
        var result = PathHelper.ParsePath("");
        Assert.Empty(result);
    }

    [Fact]
    public void ParsePath_Null_ReturnsEmptyList()
    {
        var result = PathHelper.ParsePath(null);
        Assert.Empty(result);
    }

    [Fact]
    public void ParsePath_SingleCallsign_ParsesCorrectly()
    {
        var result = PathHelper.ParsePath("WIDE1-1");
        Assert.Single(result);
        Assert.Equal("WIDE1", result[0].Base);
        Assert.Equal(1, result[0].Ssid);
    }

    [Fact]
    public void ParsePath_MultipleCallsigns_ParsesCorrectly()
    {
        var result = PathHelper.ParsePath("WIDE1-1,WIDE2-1");
        Assert.Equal(2, result.Count);
        Assert.Equal("WIDE1", result[0].Base);
        Assert.Equal(1, result[0].Ssid);
        Assert.Equal("WIDE2", result[1].Base);
        Assert.Equal(1, result[1].Ssid);
    }

    [Fact]
    public void ParsePath_CallsignWithoutSsid_ParsesCorrectly()
    {
        var result = PathHelper.ParsePath("RELAY");
        Assert.Single(result);
        Assert.Equal("RELAY", result[0].Base);
        Assert.Null(result[0].Ssid);
    }

    [Fact]
    public void ParsePath_MixedCallsigns_ParsesCorrectly()
    {
        var result = PathHelper.ParsePath("RELAY,WIDE1-1,N0CALL");
        Assert.Equal(3, result.Count);
        Assert.Equal("RELAY", result[0].Base);
        Assert.Null(result[0].Ssid);
        Assert.Equal("WIDE1", result[1].Base);
        Assert.Equal(1, result[1].Ssid);
        Assert.Equal("N0CALL", result[2].Base);
        Assert.Null(result[2].Ssid);
    }

    [Fact]
    public void ParsePath_WithSpaces_TrimsCorrectly()
    {
        var result = PathHelper.ParsePath(" WIDE1-1 , WIDE2-1 ");
        Assert.Equal(2, result.Count);
        Assert.Equal("WIDE1", result[0].Base);
        Assert.Equal("WIDE2", result[1].Base);
    }

    [Fact]
    public void ParsePath_InvalidSsid_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => PathHelper.ParsePath("WIDE1-99"));
    }

    [Fact]
    public void ParsePath_InvalidCallsign_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => PathHelper.ParsePath("TOOLONG7"));
    }

    [Fact]
    public void FormatPath_EmptyList_ReturnsEmptyString()
    {
        var result = PathHelper.FormatPath(Array.Empty<Callsign>());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FormatPath_SingleCallsign_FormatsCorrectly()
    {
        var path = new[] { new Callsign("WIDE1", 1) };
        var result = PathHelper.FormatPath(path);
        Assert.Equal("WIDE1-1", result);
    }

    [Fact]
    public void FormatPath_MultipleCallsigns_FormatsCorrectly()
    {
        var path = new[] { new Callsign("WIDE1", 1), new Callsign("WIDE2", 1) };
        var result = PathHelper.FormatPath(path);
        Assert.Equal("WIDE1-1,WIDE2-1", result);
    }

    [Fact]
    public void FormatPath_CallsignWithoutSsid_FormatsCorrectly()
    {
        var path = new[] { new Callsign("RELAY", null) };
        var result = PathHelper.FormatPath(path);
        Assert.Equal("RELAY", result);
    }

    [Fact]
    public void MatchesAlias_MatchingCallsign_ReturnsTrue()
    {
        var callsign = new Callsign("WIDE1", 1);
        Assert.True(PathHelper.MatchesAlias(callsign, "WIDE1"));
    }

    [Fact]
    public void MatchesAlias_NonMatchingCallsign_ReturnsFalse()
    {
        var callsign = new Callsign("WIDE2", 1);
        Assert.False(PathHelper.MatchesAlias(callsign, "WIDE1"));
    }

    [Fact]
    public void MatchesAlias_CaseInsensitive_ReturnsTrue()
    {
        var callsign = new Callsign("WIDE1", 1);
        Assert.True(PathHelper.MatchesAlias(callsign, "wide1"));
    }

    [Fact]
    public void DecrementSsid_WithSsid_DecrementsCorrectly()
    {
        var callsign = new Callsign("WIDE2", 2);
        var result = PathHelper.DecrementSsid(callsign);
        Assert.NotNull(result);
        Assert.Equal("WIDE2", result.Value.Base);
        Assert.Equal(1, result.Value.Ssid);
    }

    [Fact]
    public void DecrementSsid_WithSsidOne_ReturnsZero()
    {
        var callsign = new Callsign("WIDE1", 1);
        var result = PathHelper.DecrementSsid(callsign);
        Assert.NotNull(result);
        Assert.Equal("WIDE1", result.Value.Base);
        Assert.Equal(0, result.Value.Ssid);
    }

    [Fact]
    public void DecrementSsid_WithSsidZero_ReturnsNull()
    {
        var callsign = new Callsign("WIDE1", 0);
        var result = PathHelper.DecrementSsid(callsign);
        Assert.Null(result);
    }

    [Fact]
    public void DecrementSsid_WithoutSsid_ReturnsNull()
    {
        var callsign = new Callsign("RELAY", null);
        var result = PathHelper.DecrementSsid(callsign);
        Assert.Null(result);
    }

    [Fact]
    public void ParsePath_RoundTrip_PreservesData()
    {
        var original = "WIDE1-1,WIDE2-1";
        var parsed = PathHelper.ParsePath(original);
        var formatted = PathHelper.FormatPath(parsed);
        Assert.Equal(original, formatted);
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using System;
using System.Collections.Generic;
using Xunit;

namespace AetherAprs.Tests.Modems.Aprs;

public class PathHelperTests
{
    [Fact]
    public void ParsePath_EmptyString_ReturnsEmptyList()
    {
        IReadOnlyList<Callsign> result = PathHelper.ParsePath("");
        Assert.Empty(result);
    }

    [Fact]
    public void ParsePath_Null_ReturnsEmptyList()
    {
        IReadOnlyList<Callsign> result = PathHelper.ParsePath(null);
        Assert.Empty(result);
    }

    [Fact]
    public void ParsePath_SingleCallsign_ParsesCorrectly()
    {
        IReadOnlyList<Callsign> result = PathHelper.ParsePath("WIDE1-1");
        Callsign item = Assert.Single(result);
        Assert.Equal("WIDE1", item.Base);
        Assert.Equal(1, result[0].Ssid);
    }

    [Fact]
    public void ParsePath_MultipleCallsigns_ParsesCorrectly()
    {
        IReadOnlyList<Callsign> result = PathHelper.ParsePath("WIDE1-1,WIDE2-1");
        Assert.Equal(2, result.Count);
        Assert.Equal("WIDE1", result[0].Base);
        Assert.Equal(1, result[0].Ssid);
        Assert.Equal("WIDE2", result[1].Base);
        Assert.Equal(1, result[1].Ssid);
    }

    [Fact]
    public void ParsePath_CallsignWithoutSsid_ParsesCorrectly()
    {
        IReadOnlyList<Callsign> result = PathHelper.ParsePath("RELAY");
        Callsign item = Assert.Single(result);
        Assert.Equal("RELAY", item.Base);
        Assert.Null(result[0].Ssid);
    }

    [Fact]
    public void ParsePath_MixedCallsigns_ParsesCorrectly()
    {
        IReadOnlyList<Callsign> result = PathHelper.ParsePath("RELAY,WIDE1-1,N0CALL");
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
        IReadOnlyList<Callsign> result = PathHelper.ParsePath(" WIDE1-1 , WIDE2-1 ");
        Assert.Equal(2, result.Count);
        Assert.Equal("WIDE1", result[0].Base);
        Assert.Equal("WIDE2", result[1].Base);
    }

    [Fact]
    public void ParsePath_InvalidSsid_ThrowsArgumentException() => Assert.Throws<ArgumentException>(() => PathHelper.ParsePath("WIDE1-99"));

    [Fact]
    public void ParsePath_InvalidCallsign_ThrowsArgumentException() => Assert.Throws<ArgumentException>(() => PathHelper.ParsePath("TOOLONG7"));

    [Fact]
    public void FormatPath_EmptyList_ReturnsEmptyString()
    {
        string result = PathHelper.FormatPath([]);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FormatPath_SingleCallsign_FormatsCorrectly()
    {
        Callsign[] path = [new Callsign("WIDE1", 1)];
        string result = PathHelper.FormatPath(path);
        Assert.Equal("WIDE1-1", result);
    }

    [Fact]
    public void FormatPath_MultipleCallsigns_FormatsCorrectly()
    {
        Callsign[] path = [new Callsign("WIDE1", 1), new Callsign("WIDE2", 1)];
        string result = PathHelper.FormatPath(path);
        Assert.Equal("WIDE1-1,WIDE2-1", result);
    }

    [Fact]
    public void FormatPath_CallsignWithoutSsid_FormatsCorrectly()
    {
        Callsign[] path = [new Callsign("RELAY", null)];
        string result = PathHelper.FormatPath(path);
        Assert.Equal("RELAY", result);
    }

    [Fact]
    public void MatchesAlias_MatchingCallsign_ReturnsTrue()
    {
        Callsign callsign = new("WIDE1", 1);
        Assert.True(PathHelper.MatchesAlias(callsign, "WIDE1"));
    }

    [Fact]
    public void MatchesAlias_NonMatchingCallsign_ReturnsFalse()
    {
        Callsign callsign = new("WIDE2", 1);
        Assert.False(PathHelper.MatchesAlias(callsign, "WIDE1"));
    }

    [Fact]
    public void MatchesAlias_CaseInsensitive_ReturnsTrue()
    {
        Callsign callsign = new("WIDE1", 1);
        Assert.True(PathHelper.MatchesAlias(callsign, "wide1"));
    }

    [Fact]
    public void DecrementSsid_WithSsid_DecrementsCorrectly()
    {
        Callsign callsign = new("WIDE2", 2);
        Callsign? result = PathHelper.DecrementSsid(callsign);
        Assert.NotNull(result);
        Assert.Equal("WIDE2", result.Value.Base);
        Assert.Equal(1, result.Value.Ssid);
    }

    [Fact]
    public void DecrementSsid_WithSsidOne_ReturnsZero()
    {
        Callsign callsign = new("WIDE1", 1);
        Callsign? result = PathHelper.DecrementSsid(callsign);
        Assert.NotNull(result);
        Assert.Equal("WIDE1", result.Value.Base);
        Assert.Equal(0, result.Value.Ssid);
    }

    [Fact]
    public void DecrementSsid_WithSsidZero_ReturnsNull()
    {
        Callsign callsign = new("WIDE1", 0);
        Callsign? result = PathHelper.DecrementSsid(callsign);
        Assert.Null(result);
    }

    [Fact]
    public void DecrementSsid_WithoutSsid_ReturnsNull()
    {
        Callsign callsign = new("RELAY", null);
        Callsign? result = PathHelper.DecrementSsid(callsign);
        Assert.Null(result);
    }

    [Fact]
    public void DecrementSsid_RepeatedCallsign_PreservesRepeatedFlag()
    {
        Callsign callsign = new("WIDE2", 2, hasBeenRepeated: true);
        Callsign? result = PathHelper.DecrementSsid(callsign);

        Assert.NotNull(result);
        Assert.Equal(1, result.Value.Ssid);
        Assert.True(result.Value.HasBeenRepeated);
    }

    [Fact]
    public void ParsePath_RoundTrip_PreservesData()
    {
        string original = "WIDE1-1,WIDE2-1";
        IReadOnlyList<Callsign> parsed = PathHelper.ParsePath(original);
        string formatted = PathHelper.FormatPath(parsed);
        Assert.Equal(original, formatted);
    }
}

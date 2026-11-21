// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models.Aprs;
using Xunit;

namespace AetherAprs.Tests.Models;

public class AprsIsFilterTests
{
    [Fact]
    public void ToFilterString_EmptyFilter_ReturnsEmptyString()
    {
        var filter = new AprsIsFilter();

        Assert.Equal(string.Empty, filter.ToFilterString());
    }

    [Fact]
    public void ToFilterString_RangeOnly_ProducesRangeDirective()
    {
        var filter = new AprsIsFilter
        {
            RangeLatitude = 38.7223,
            RangeLongitude = -9.1393,
            RangeDistanceKm = 50
        };

        Assert.Equal("r/38.7223/-9.1393/50", filter.ToFilterString());
    }

    [Fact]
    public void ToFilterString_AllDirectives_UsesCanonicalOrder()
    {
        var filter = new AprsIsFilter
        {
            RangeLatitude = 38.7223,
            RangeLongitude = -9.1393,
            RangeDistanceKm = 50,
            MyRangeKm = 25,
            PacketTypes = AprsIsPacketType.Position | AprsIsPacketType.Message | AprsIsPacketType.Weather,
        };
        filter.Prefixes.Add("CT");
        filter.Prefixes.Add("CU");
        filter.Buddies.Add("CT1ABC");
        filter.AreaNorthLatitude = 40;
        filter.AreaWestLongitude = -10;
        filter.AreaSouthLatitude = 37;
        filter.AreaEastLongitude = -6;

        var result = filter.ToFilterString();

        Assert.Equal(
            "r/38.7223/-9.1393/50 p/CT/CU b/CT1ABC t/pmw a/40/-10/37/-6 m/25",
            result);
    }

    [Fact]
    public void ToFilterString_PreservesOtherDirectives_AfterKnownDirectives()
    {
        var filter = new AprsIsFilter { MyRangeKm = 10 };
        filter.OtherDirectives.Add("q/C");
        filter.OtherDirectives.Add("-p/K");

        Assert.Equal("m/10 q/C -p/K", filter.ToFilterString());
    }

    [Fact]
    public void ToFilterString_PacketTypes_FollowCanonicalTypeOrder()
    {
        var filter = new AprsIsFilter
        {
            PacketTypes = AprsIsPacketType.Weather | AprsIsPacketType.Position | AprsIsPacketType.Status
        };

        Assert.Equal("t/psw", filter.ToFilterString());
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyFilter()
    {
        var filter = AprsIsFilter.Parse(string.Empty);

        Assert.False(filter.RangeLatitude.HasValue);
        Assert.False(filter.MyRangeKm.HasValue);
        Assert.Equal(AprsIsPacketType.None, filter.PacketTypes);
        Assert.Empty(filter.OtherDirectives);
    }

    [Fact]
    public void Parse_Null_ReturnsEmptyFilter()
    {
        var filter = AprsIsFilter.Parse(null!);

        Assert.Equal(string.Empty, filter.ToFilterString());
    }

    [Fact]
    public void Parse_RangeDirective_PopulatesRangeFields()
    {
        var filter = AprsIsFilter.Parse("r/38.7223/-9.1393/50");

        Assert.Equal(38.7223, filter.RangeLatitude);
        Assert.Equal(-9.1393, filter.RangeLongitude);
        Assert.Equal(50, filter.RangeDistanceKm);
    }

    [Fact]
    public void Parse_MultipleDirectives_PopulatesAllFields()
    {
        var filter = AprsIsFilter.Parse("r/38.7/-9.1/50 p/CT/CU b/CT1ABC t/pmw a/40/-10/37/-6 m/25");

        Assert.Equal(38.7, filter.RangeLatitude);
        Assert.Equal(new[] { "CT", "CU" }, filter.Prefixes);
        Assert.Equal(new[] { "CT1ABC" }, filter.Buddies);
        Assert.Equal(AprsIsPacketType.Position | AprsIsPacketType.Message | AprsIsPacketType.Weather, filter.PacketTypes);
        Assert.Equal(40, filter.AreaNorthLatitude);
        Assert.Equal(-10, filter.AreaWestLongitude);
        Assert.Equal(37, filter.AreaSouthLatitude);
        Assert.Equal(-6, filter.AreaEastLongitude);
        Assert.Equal(25, filter.MyRangeKm);
    }

    [Fact]
    public void Parse_UnknownDirective_IsPreservedVerbatim()
    {
        var filter = AprsIsFilter.Parse("m/50 q/C -p/K");

        Assert.Equal(50, filter.MyRangeKm);
        Assert.Equal(new[] { "q/C", "-p/K" }, filter.OtherDirectives);
    }

    [Fact]
    public void Parse_ThenToFilterString_RoundTrips()
    {
        const string original = "r/38.7223/-9.1393/50 p/CT/CU b/CT1ABC t/pmw a/40/-10/37/-6 m/25";

        var filter = AprsIsFilter.Parse(original);

        Assert.Equal(original, filter.ToFilterString());
    }

    [Fact]
    public void Parse_TypeCharacters_CaseInsensitive()
    {
        var filter = AprsIsFilter.Parse("t/PMW");

        Assert.Equal(AprsIsPacketType.Position | AprsIsPacketType.Message | AprsIsPacketType.Weather, filter.PacketTypes);
        Assert.Equal("t/pmw", filter.ToFilterString());
    }

    [Fact]
    public void Parse_MalformedRange_IsPreservedVerbatim()
    {
        var filter = AprsIsFilter.Parse("r/38.7/-9.1");

        Assert.False(filter.RangeLatitude.HasValue);
        Assert.Equal(new[] { "r/38.7/-9.1" }, filter.OtherDirectives);
    }

    [Fact]
    public void Parse_MalformedType_IsPreservedVerbatim()
    {
        var filter = AprsIsFilter.Parse("t/pzx");

        Assert.Equal(AprsIsPacketType.None, filter.PacketTypes);
        Assert.Equal(new[] { "t/pzx" }, filter.OtherDirectives);
    }

    [Fact]
    public void NormalizeForConnection_AllTypesAndMyRange_ResolvesToFixedRange()
    {
        // Legacy filter: t/ selecting every type is a no-op that would flood,
        // and m/ cannot match on a read-only connection.
        var result = AprsIsFilter.NormalizeForConnection("t/poimqstunw m/100", 38.7223, -9.1393);

        Assert.Equal("r/38.7223/-9.1393/100", result);
    }

    [Fact]
    public void NormalizeForConnection_AllTypesWithoutLocation_StripsTypesOnly()
    {
        var result = AprsIsFilter.NormalizeForConnection("t/poimqstunw m/100", null, null);

        Assert.Equal("m/100", result);
    }

    [Fact]
    public void NormalizeForConnection_SubsetTypesWithLocation_KeepsTypesAndResolvesMyRange()
    {
        var result = AprsIsFilter.NormalizeForConnection("t/pmw m/50", 38.7223, -9.1393);

        Assert.Equal("r/38.7223/-9.1393/50 t/pmw", result);
    }

    [Fact]
    public void NormalizeForConnection_FixedRangeAlreadyPresent_KeepsItAndDropsMyRange()
    {
        var result = AprsIsFilter.NormalizeForConnection("r/1/2/30 m/50", 38.7223, -9.1393);

        Assert.Equal("r/1/2/30", result);
    }

    [Fact]
    public void NormalizeForConnection_EmptyFilter_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, AprsIsFilter.NormalizeForConnection(string.Empty, 38.7223, -9.1393));
    }

    [Fact]
    public void NormalizeForConnection_FixedRangeOnly_IsUnchanged()
    {
        var result = AprsIsFilter.NormalizeForConnection("r/38.7/-9.1/50", 10, 20);

        Assert.Equal("r/38.7/-9.1/50", result);
    }
}

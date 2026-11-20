// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.ViewModels.Components;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Components;

public class AprsIsFilterBuilderViewModelTests
{
    [Fact]
    public void Defaults_ProduceEmptyFilter()
    {
        var vm = new AprsIsFilterBuilderViewModel();

        Assert.Equal(string.Empty, vm.Filter);
    }

    [Fact]
    public void SetMyRange_ProducesMyRangeDirective()
    {
        var vm = new AprsIsFilterBuilderViewModel { MyRangeKm = "50" };

        Assert.Equal("m/50", vm.Filter);
    }

    [Fact]
    public void SetRangeFields_ProducesRangeDirective()
    {
        var vm = new AprsIsFilterBuilderViewModel
        {
            RangeLatitude = "38.7223",
            RangeLongitude = "-9.1393",
            RangeDistanceKm = "50"
        };

        Assert.Equal("r/38.7223/-9.1393/50", vm.Filter);
    }

    [Fact]
    public void SetPrefixesAndBuddies_ProducesDirectives()
    {
        var vm = new AprsIsFilterBuilderViewModel
        {
            Prefixes = "CT CU",
            Buddies = "CT1ABC"
        };

        Assert.Equal("p/CT/CU b/CT1ABC", vm.Filter);
    }

    [Fact]
    public void SetPacketTypes_ProducesTypeDirective()
    {
        var vm = new AprsIsFilterBuilderViewModel
        {
            IncludePosition = true,
            IncludeMessage = true,
            IncludeWeather = true
        };

        Assert.Equal("t/pmw", vm.Filter);
    }

    [Fact]
    public void SetAreaFields_ProducesAreaDirective()
    {
        var vm = new AprsIsFilterBuilderViewModel
        {
            AreaNorth = "40",
            AreaWest = "-10",
            AreaSouth = "37",
            AreaEast = "-6"
        };

        Assert.Equal("a/40/-10/37/-6", vm.Filter);
    }

    [Fact]
    public void IncompleteRangeFields_AreIgnored()
    {
        var vm = new AprsIsFilterBuilderViewModel
        {
            RangeLatitude = "38.7223",
            RangeLongitude = "-9.1393"
        };

        Assert.Equal(string.Empty, vm.Filter);
    }

    [Fact]
    public void Load_EmptyString_ClearsAllFields()
    {
        var vm = new AprsIsFilterBuilderViewModel { MyRangeKm = "50" };
        vm.Load(string.Empty);

        Assert.Equal(string.Empty, vm.Filter);
        Assert.Equal(string.Empty, vm.MyRangeKm);
    }

    [Fact]
    public void Load_RangeDirective_PopulatesFields()
    {
        var vm = new AprsIsFilterBuilderViewModel();
        vm.Load("r/38.7223/-9.1393/50");

        Assert.Equal("38.7223", vm.RangeLatitude);
        Assert.Equal("-9.1393", vm.RangeLongitude);
        Assert.Equal("50", vm.RangeDistanceKm);
    }

    [Fact]
    public void Load_PacketTypes_SetsIncludeFlags()
    {
        var vm = new AprsIsFilterBuilderViewModel();
        vm.Load("t/pmw");

        Assert.True(vm.IncludePosition);
        Assert.True(vm.IncludeMessage);
        Assert.True(vm.IncludeWeather);
        Assert.False(vm.IncludeStatus);
    }

    [Fact]
    public void Load_ThenFilter_RoundTrips()
    {
        var vm = new AprsIsFilterBuilderViewModel();
        vm.Load("r/38.7223/-9.1393/50 p/CT/CU b/CT1ABC t/pmw a/40/-10/37/-6 m/25");

        Assert.Equal("r/38.7223/-9.1393/50 p/CT/CU b/CT1ABC t/pmw a/40/-10/37/-6 m/25", vm.Filter);
    }

    [Fact]
    public void Load_WithUnknownDirective_PreservesIt()
    {
        var vm = new AprsIsFilterBuilderViewModel();
        vm.Load("m/50 q/C");

        Assert.Equal("m/50 q/C", vm.Filter);
    }
}

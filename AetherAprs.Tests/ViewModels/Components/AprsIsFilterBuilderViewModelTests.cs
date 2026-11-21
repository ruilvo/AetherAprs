// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.ViewModels.Components;
using Xunit;

namespace AetherAprs.Tests.ViewModels.Components;

public class AprsIsFilterBuilderViewModelTests
{
    [Fact]
    public void Defaults_SelectAllPacketTypesAndUse100KmDistance()
    {
        var vm = new AprsIsFilterBuilderViewModel();

        // All types selected is a no-op type filter, so only the distance is emitted.
        Assert.Equal("m/100", vm.Filter);
        Assert.Equal("100", vm.MyRangeKm);
        Assert.True(vm.IncludePosition);
        Assert.True(vm.IncludeWeather);
    }

    [Fact]
    public void SetMyRange_ProducesMyRangeDirective()
    {
        var vm = new AprsIsFilterBuilderViewModel { MyRangeKm = "50" };

        Assert.Equal("m/50", vm.Filter);
    }

    [Fact]
    public void SetDistancePrefixesAndTypes_ProducesOnlySupportedDirectives()
    {
        var vm = new AprsIsFilterBuilderViewModel
        {
            MyRangeKm = "50",
            Prefixes = "CT CU",
            IncludePosition = true,
            IncludeMessage = true
        };

        Assert.Equal("p/CT/CU m/50", vm.Filter);
    }

    [Fact]
    public void SetPrefixes_ProducesPrefixDirective()
    {
        var vm = new AprsIsFilterBuilderViewModel
        {
            Prefixes = "CT CU"
        };

        Assert.Equal("p/CT/CU m/100", vm.Filter);
    }

    [Fact]
    public void SetPacketTypes_ProducesTypeDirective()
    {
        var vm = new AprsIsFilterBuilderViewModel
        {
            IncludePosition = true,
            IncludeObject = false,
            IncludeItem = false,
            IncludeMessage = true,
            IncludeQuery = false,
            IncludeStatus = false,
            IncludeTelemetry = false,
            IncludeUserDefined = false,
            IncludeNws = false,
            IncludeWeather = true
        };

        Assert.Equal("t/pmw m/100", vm.Filter);
    }

    [Fact]
    public void Load_IgnoresPointAreaBuddyAndUnknownDirectives()
    {
        var vm = new AprsIsFilterBuilderViewModel();
        vm.Load("r/38.7223/-9.1393/50 p/CT b/CT1ABC t/pm a/40/-10/37/-6 m/25 q/C");

        Assert.Equal("p/CT t/pm m/25", vm.Filter);
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
    public void Load_RangeDirective_DoesNotPopulatePointFields()
    {
        var vm = new AprsIsFilterBuilderViewModel();
        vm.Load("r/38.7223/-9.1393/50");

        Assert.Equal(string.Empty, vm.Filter);
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

        Assert.Equal("p/CT/CU t/pmw m/25", vm.Filter);
    }

    [Fact]
    public void Load_WithUnknownDirective_IgnoresIt()
    {
        var vm = new AprsIsFilterBuilderViewModel();
        vm.Load("m/50 q/C");

        Assert.Equal("m/50", vm.Filter);
    }
}

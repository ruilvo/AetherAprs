// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Configuration;
using AetherAprs.ViewModels;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class PortItemViewModelTests
{
    [Fact]
    public void Constructor_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PortItemViewModel(null!, _ => { }, _ => { }, _ => { }, _ => { }));
    }

    [Fact]
    public void TypeName_MapsAprsIs()
    {
        var vm = Create(new PortConfig { Name = "Port", TypeSettings = new AprsIsSettings() });
        Assert.Equal("APRS-IS", vm.TypeName);
    }

    [Fact]
    public void TypeName_MapsKiss()
    {
        var vm = Create(new PortConfig { Name = "Port", TypeSettings = new KissSettings() });
        Assert.Equal("KISS", vm.TypeName);
    }

    [Fact]
    public void TypeName_UnknownWhenNull()
    {
        var vm = Create(new PortConfig { Name = "Port", TypeSettings = null });
        Assert.Equal("Unknown", vm.TypeName);
    }

    [Fact]
    public void StatusText_TracksIsEnabled()
    {
        var vm = Create(new PortConfig { Name = "Port", IsEnabled = false });

        Assert.Equal("Stopped", vm.StatusText);

        vm.IsEnabled = true;

        Assert.Equal("Running", vm.StatusText);
    }

    [Fact]
    public void PropertyChanges_InvokeCallbacks()
    {
        PortItemViewModel? toggled = null;
        PortItemViewModel? mapToggled = null;
        var config = new PortConfig { Name = "Port", IsEnabled = false, ShowOnMap = false };

        var vm = new PortItemViewModel(
            config,
            item => toggled = item,
            item => mapToggled = item,
            _ => { },
            _ => { });

        vm.IsEnabled = true;
        vm.ShowOnMap = true;

        Assert.Same(vm, toggled);
        Assert.Same(vm, mapToggled);
    }

    [Fact]
    public void DeleteAndEdit_InvokeCallbacks()
    {
        PortItemViewModel? deleted = null;
        PortItemViewModel? edited = null;
        var vm = new PortItemViewModel(
            new PortConfig { Name = "Port" },
            _ => { },
            _ => { },
            item => deleted = item,
            item => edited = item);

        vm.Delete();
        vm.Edit();

        Assert.Same(vm, deleted);
        Assert.Same(vm, edited);
    }

    [Fact]
    public void BuildConfig_WritesFlagsOntoSameInstance()
    {
        var config = new PortConfig { Name = "Port", IsEnabled = false, ShowOnMap = false };
        var vm = Create(config);

        vm.IsEnabled = true;
        vm.ShowOnMap = true;
        var built = vm.BuildConfig();

        Assert.Same(config, built);
        Assert.True(built.IsEnabled);
        Assert.True(built.ShowOnMap);
    }

    private static PortItemViewModel Create(PortConfig config) =>
        new(config, _ => { }, _ => { }, _ => { }, _ => { });
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.ViewModels;
using Xunit;
using Xunit.Sdk;

namespace AetherAprs.Tests.ViewModels;

public sealed class PortItemViewModelTests : TestFixtureBase
{
    [Fact]
    public void Constructor_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PortItemViewModel(null!, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => { }, logger: null));
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
    public async Task PropertyChanges_InvokeCallbacks()
    {
        PortItemViewModel? toggled = null;
        PortItemViewModel? mapToggled = null;
        var config = new PortConfig { Name = "Port", IsEnabled = false, ShowOnMap = false };

        var vm = new PortItemViewModel(
            config,
            item => { toggled = item; return Task.CompletedTask; },
            item => { mapToggled = item; return Task.CompletedTask; },
            _ => Task.CompletedTask,
            _ => { },
            logger: null);

        vm.IsEnabled = true;
        vm.ShowOnMap = true;

        // Give async handlers time to complete
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Same(vm, toggled);
        Assert.Same(vm, mapToggled);
    }

    [Fact]
    public async Task DeleteAndEdit_InvokeCallbacks()
    {
        PortItemViewModel? deleted = null;
        PortItemViewModel? edited = null;
        var vm = new PortItemViewModel(
            new PortConfig { Name = "Port" },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            item => { deleted = item; return Task.CompletedTask; },
            item => edited = item,
            logger: null);

        await vm.DeleteCommand.ExecuteAsync(null);
        vm.EditCommand.Execute(null);

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

    [Fact]
    public void Constructor_DoesNotInvokeCallbacksDuringInitialization()
    {
        // This test prevents the infinite loop bug where setting properties
        // during construction triggered callbacks that reloaded the ports list
        var toggleCount = 0;
        var mapToggleCount = 0;
        
        var config = new PortConfig 
        { 
            Name = "Port", 
            IsEnabled = true,  // Setting to true during construction
            ShowOnMap = true   // Setting to true during construction
        };

        var vm = new PortItemViewModel(
            config,
            _ => { toggleCount++; return Task.CompletedTask; },
            _ => { mapToggleCount++; return Task.CompletedTask; },
            _ => Task.CompletedTask,
            _ => { },
            logger: null);

        // Callbacks should NOT have been invoked during construction
        Assert.Equal(0, toggleCount);
        Assert.Equal(0, mapToggleCount);
        
        // But properties should be set correctly
        Assert.True(vm.IsEnabled);
        Assert.True(vm.ShowOnMap);
    }

    [Fact]
    public async Task PropertyChanges_AfterInitialization_InvokeCallbacks()
    {
        // This test ensures callbacks ARE invoked after initialization
        var toggleCount = 0;
        var mapToggleCount = 0;
        var config = new PortConfig { Name = "Port", IsEnabled = false, ShowOnMap = false };

        var vm = new PortItemViewModel(
            config,
            _ => { toggleCount++; return Task.CompletedTask; },
            _ => { mapToggleCount++; return Task.CompletedTask; },
            _ => Task.CompletedTask,
            _ => { },
            logger: null);

        // Verify no callbacks during construction
        Assert.Equal(0, toggleCount);
        Assert.Equal(0, mapToggleCount);

        // Now change properties - callbacks SHOULD be invoked
        vm.IsEnabled = true;
        await Task.Delay(50, TestContext.Current.CancellationToken); // Give async handler time to complete
        Assert.Equal(1, toggleCount);
        Assert.Equal(0, mapToggleCount);

        vm.ShowOnMap = true;
        await Task.Delay(50, TestContext.Current.CancellationToken); // Give async handler time to complete
        Assert.Equal(1, toggleCount);
        Assert.Equal(1, mapToggleCount);
    }

    private static PortItemViewModel Create(PortConfig config) =>
        new(config, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => { }, logger: null);
}

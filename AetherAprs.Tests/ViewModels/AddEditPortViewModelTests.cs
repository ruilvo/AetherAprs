// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Services;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Transports.Kiss;
using AetherAprs.ViewModels.Pages;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class AddEditPortViewModelTests
{
    [Fact]
    public void DefaultsEnableTransmitAndInheritBeaconMode()
    {
        var viewModel = CreateViewModel();
        Assert.True(viewModel.IsTx);

        var config = viewModel.BuildConfig();
        Assert.True(config.IsTx);
        Assert.Null(config.DynamicBeaconMode);
    }

    [Fact]
    public void BuildConfigCanInheritDefaultBeaconMode()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedBeaconMode = BeaconModeOption.UseDefault;

        var config = viewModel.BuildConfig();
        Assert.Null(config.DynamicBeaconMode);
    }

    [Fact]
    public void BuildConfigSupportsCustomBeaconMode()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedBeaconMode = BeaconModeOption.FromMode(AetherAprs.Models.DynamicBeaconMode.Walk);

        var config = viewModel.BuildConfig();
        Assert.Equal(AetherAprs.Models.DynamicBeaconMode.Walk, config.DynamicBeaconMode);
    }

    [Fact]
    public void PopulateFromAprsIsConfig()
    {
        var viewModel = CreateViewModel();
        var config = new PortConfig
        {
            Name = "Test Port",
            TypeSettings = new AprsIsSettings
            {
                Server = "example.com",
                ServerPort = 14580,
                Passcode = "12345",
                Filter = "r/50/0/100"
            },
            IsRx = true,
            IsTx = false,
            ShowOnMap = true,
            DynamicBeaconMode = AetherAprs.Models.DynamicBeaconMode.Drive
        };

        viewModel.PopulateFrom(config);

        Assert.Equal("Test Port", viewModel.Name);
        Assert.Equal(typeof(AprsIsSettings), viewModel.SelectedPortSettingsType);
        Assert.Equal("example.com", viewModel.Server);
        Assert.Equal(14580, viewModel.ServerPort);
        Assert.Equal("12345", viewModel.Passcode);
        Assert.Equal("r/50/0/100", viewModel.Filter);
        Assert.True(viewModel.IsRx);
        Assert.False(viewModel.IsTx);
        Assert.True(viewModel.ShowOnMap);
        Assert.Equal(AetherAprs.Models.DynamicBeaconMode.Drive, viewModel.SelectedBeaconMode.Mode);
    }

    [Fact]
    public void PopulateFromKissTcpConfig()
    {
        var viewModel = CreateViewModel();
        var config = new PortConfig
        {
            Name = "KISS Port",
            TypeSettings = new KissSettings
            {
                Transport = new TcpKissTransportSettings
                {
                    Host = "192.168.1.100",
                    Port = 8001
                }
            }
        };

        viewModel.PopulateFrom(config);

        Assert.Equal("KISS Port", viewModel.Name);
        Assert.Equal(typeof(KissSettings), viewModel.SelectedPortSettingsType);
        Assert.Equal(typeof(TcpKissTransportSettings), viewModel.SelectedKissTransportType);
        Assert.Equal("192.168.1.100", viewModel.TcpHost);
        Assert.Equal(8001, viewModel.TcpPort);
    }

    [Fact]
    public void InitializeWithoutExistingConfigKeepsAddPortTitle()
    {
        var viewModel = CreateViewModel(initialize: false);

        viewModel.Initialize("N0CALL", 1);

        Assert.False(viewModel.IsEditing);
        Assert.Equal("APRS-IS Port 1", viewModel.Name);
        Assert.Equal("Add Port", viewModel.Title);
    }

    [Fact]
    public void BeaconModesIncludeUseDefaultOption()
    {
        var viewModel = CreateViewModel();

        Assert.Contains(viewModel.BeaconModes, option => option.Mode is null && option.DisplayName == "<Use default>");
        Assert.Equal(BeaconModeOption.UseDefault, viewModel.SelectedBeaconMode);
    }

    [Fact]
    public void InitializeWithExistingConfigUsesEditTitle()
    {
        var viewModel = CreateViewModel(initialize: false);
        var existing = new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "Existing Port",
            TypeSettings = new AprsIsSettings()
        };

        viewModel.Initialize("N0CALL", 1, existing);

        Assert.True(viewModel.IsEditing);
        Assert.Equal("Edit Existing Port", viewModel.Title);
    }

    private static AddEditPortViewModel CreateViewModel(bool initialize = true)
    {
        var nav = Substitute.For<INavigationService>();
        var portService = Substitute.For<IPortService>();
        var kissFactory = new KissStreamFactory(new List<IKissStreamConnector>());
        var bleScanner = Substitute.For<IBluetoothLeScanner>();
        var btClassic = Substitute.For<IBluetoothClassicDeviceProvider>();
        
        var vm = new AddEditPortViewModel(nav, portService, kissFactory, bleScanner, btClassic);
        if (initialize)
        {
            vm.Initialize("N0CALL", 1);
        }
        return vm;
    }
}

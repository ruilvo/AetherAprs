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
    public void DefaultsUseHumanSymbol()
    {
        var viewModel = CreateViewModel();

        Assert.Equal("/", viewModel.SymbolTableCharacter);
        Assert.Equal("[", viewModel.SymbolCodeCharacter);
        Assert.True(viewModel.IsSymbolValid);

        var config = viewModel.BuildConfig();
        Assert.Null(config.SymbolTableCharacter);
        Assert.Null(config.SymbolCodeCharacter);
    }

    [Fact]
    public void ChangingSymbolCharactersUpdatesConfig()
    {
        var viewModel = CreateViewModel();

        viewModel.SymbolTableCharacter = "\\";
        viewModel.SymbolCodeCharacter = ">";
        viewModel.UseDefaultSymbol = false;

        Assert.True(viewModel.IsSymbolValid);
        var config = viewModel.BuildConfig();
        Assert.Equal("\\", config.SymbolTableCharacter);
        Assert.Equal(">", config.SymbolCodeCharacter);
    }

    [Fact]
    public void BuildConfigCanInheritDefaultSymbolAndBeaconMode()
    {
        var viewModel = CreateViewModel();
        viewModel.UseDefaultSymbol = true;
        viewModel.SelectedBeaconMode = null;

        var config = viewModel.BuildConfig();

        Assert.Null(config.SymbolTableCharacter);
        Assert.Null(config.SymbolCodeCharacter);
        Assert.Null(config.DynamicBeaconMode);
    }

    [Fact]
    public void BuildConfigSupportsCustomSymbolAndBeaconMode()
    {
        var viewModel = CreateViewModel();
        viewModel.UseDefaultSymbol = false;
        viewModel.SymbolTableCharacter = "/";
        viewModel.SymbolCodeCharacter = ">";
        viewModel.SelectedBeaconMode = AetherAprs.Models.DynamicBeaconMode.Walk;

        var config = viewModel.BuildConfig();

        Assert.Equal("/", config.SymbolTableCharacter);
        Assert.Equal(">", config.SymbolCodeCharacter);
        Assert.Equal(AetherAprs.Models.DynamicBeaconMode.Walk, config.DynamicBeaconMode);
    }

    [Fact]
    public void PopulateFromAprsIsConfig()
    {
        var viewModel = CreateViewModel();
        var config = new PortConfig
        {
            Name = "Test Port",
            Ssid = 5,
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
            SymbolTableCharacter = "/",
            SymbolCodeCharacter = ">",
            DynamicBeaconMode = AetherAprs.Models.DynamicBeaconMode.Drive
        };

        viewModel.PopulateFrom(config);

        Assert.Equal("Test Port", viewModel.Name);
        Assert.Equal(5, viewModel.Ssid);
        Assert.Equal(typeof(AprsIsSettings), viewModel.SelectedPortSettingsType);
        Assert.Equal("example.com", viewModel.Server);
        Assert.Equal(14580, viewModel.ServerPort);
        Assert.Equal("12345", viewModel.Passcode);
        Assert.Equal("r/50/0/100", viewModel.Filter);
        Assert.True(viewModel.IsRx);
        Assert.False(viewModel.IsTx);
        Assert.True(viewModel.ShowOnMap);
        Assert.False(viewModel.UseDefaultSymbol);
        Assert.Equal("/", viewModel.SymbolTableCharacter);
        Assert.Equal(">", viewModel.SymbolCodeCharacter);
        Assert.Equal(AetherAprs.Models.DynamicBeaconMode.Drive, viewModel.SelectedBeaconMode);
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

    private static AddEditPortViewModel CreateViewModel()
    {
        var nav = Substitute.For<INavigationService>();
        var portService = Substitute.For<IPortService>();
        var kissFactory = new KissStreamFactory(new List<IKissStreamConnector>());
        var bleScanner = Substitute.For<IBluetoothLeScanner>();
        var btClassic = Substitute.For<IBluetoothClassicDeviceProvider>();
        
        var vm = new AddEditPortViewModel(nav, portService, kissFactory, bleScanner, btClassic);
        vm.Initialize("N0CALL", 1, "/", "[");
        return vm;
    }
}

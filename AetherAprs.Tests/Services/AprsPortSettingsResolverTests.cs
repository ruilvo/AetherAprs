// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Configuration;
using AetherAprs.Models;
using AetherAprs.Services;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services;

public class AprsPortSettingsResolverTests
{
    [Fact]
    public void GetSsid_ReturnsDefaultSsidWhenPositive()
    {
        var config = CreateConfiguration();
        config.Settings.Aprs.DefaultSsid = 3;
        var resolver = CreateResolver(config);

        Assert.Equal(3, resolver.GetSsid());
    }

    [Fact]
    public void GetSsid_ReturnsNullWhenSsidIsZero()
    {
        var config = CreateConfiguration();
        var resolver = CreateResolver(config);

        Assert.Null(resolver.GetSsid());
    }

    [Fact]
    public void GetCallsign_AppendsSsidWhenPositive()
    {
        var config = CreateConfiguration();
        config.Settings.Aprs.DefaultSsid = 7;
        var resolver = CreateResolver(config);

        Assert.Equal("N0CALL-7", resolver.GetCallsign("N0CALL"));
    }

    [Fact]
    public void GetCallsign_OmitsSsidWhenZero()
    {
        var config = CreateConfiguration();
        var resolver = CreateResolver(config);

        Assert.Equal("N0CALL", resolver.GetCallsign("N0CALL"));
    }

    [Fact]
    public void GetPortBeaconMode_ReturnsPortModeWhenSet()
    {
        var config = CreateConfiguration();
        var resolver = CreateResolver(config);
        var port = CreatePort();
        port.DynamicBeaconMode = DynamicBeaconMode.Drive;

        var result = resolver.GetPortBeaconMode(port);

        Assert.Equal(DynamicBeaconMode.Drive, result);
    }

    [Fact]
    public void GetPortBeaconMode_ReturnsActiveModeWhenPortModeIsNull()
    {
        var resolver = CreateResolver(activeMode: DynamicBeaconMode.Custom);
        var port = CreatePort();
        port.DynamicBeaconMode = null;

        var result = resolver.GetPortBeaconMode(port);

        Assert.Equal(DynamicBeaconMode.Custom, result);
    }

    [Fact]
    public void GetSymbolTableCharacter_ReturnsDefault()
    {
        var config = CreateConfiguration();
        config.Settings.Aprs.DefaultSymbolTableCharacter = "\\";
        var resolver = CreateResolver(config);

        Assert.Equal("\\", resolver.GetSymbolTableCharacter());
    }

    [Fact]
    public void GetSymbolCodeCharacter_ReturnsDefault()
    {
        var config = CreateConfiguration();
        config.Settings.Aprs.DefaultSymbolCodeCharacter = "k";
        var resolver = CreateResolver(config);

        Assert.Equal("k", resolver.GetSymbolCodeCharacter());
    }

    private static TestConfigurationService CreateConfiguration()
    {
        return new TestConfigurationService();
    }

    private static AprsPortSettingsResolver CreateResolver(
        IConfigurationService? config = null,
        DynamicBeaconMode activeMode = DynamicBeaconMode.Walk)
    {
        var beacon = Substitute.For<IBeaconService>();
        beacon.CurrentConfiguration.Returns(activeMode switch
        {
            DynamicBeaconMode.Drive => BeaconConfig.CreateDrivePreset(),
            DynamicBeaconMode.Custom => BeaconConfig.CreateCustomPreset(),
            _ => BeaconConfig.CreateWalkPreset()
        });

        return new AprsPortSettingsResolver(config ?? CreateConfiguration(), beacon);
    }

    private static PortConfig CreatePort()
    {
        return new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "Test Port",
            IsEnabled = true,
            IsRx = true,
            IsTx = true,
            TypeSettings = new AprsIsSettings()
        };
    }

    private sealed class TestConfigurationService : IConfigurationService
    {
        public AppSettings Settings { get; } = new();

        public System.Threading.Tasks.Task SaveSettingsAsync() => System.Threading.Tasks.Task.CompletedTask;
    }
}

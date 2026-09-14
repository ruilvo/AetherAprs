// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Configuration;
using AetherAprs.Models;
using AetherAprs.Services;
using Xunit;

namespace AetherAprs.Tests.Services;

public class AprsPortSettingsResolverTests
{
    [Fact]
    public void GetPortSsid_ReturnsPortSsidWhenSet()
    {
        var config = CreateConfiguration();
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        port.Ssid = 5;

        var result = resolver.GetPortSsid(port);

        Assert.Equal(5, result);
    }

    [Fact]
    public void GetPortSsid_ReturnsDefaultSsidWhenPortSsidIsNull()
    {
        var config = CreateConfiguration();
        config.Settings.Aprs.DefaultSsid = 3;
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        port.Ssid = null;

        var result = resolver.GetPortSsid(port);

        Assert.Equal(3, result);
    }

    [Fact]
    public void GetPortSsid_ReturnsNullWhenSsidIsZero()
    {
        var config = CreateConfiguration();
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        port.Ssid = 0;

        var result = resolver.GetPortSsid(port);

        Assert.Null(result);
    }

    [Fact]
    public void GetPortSsid_ReturnsNullWhenSsidIsNegative()
    {
        var config = CreateConfiguration();
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        
        // Negative SSID should throw ArgumentOutOfRangeException due to validation
        Assert.Throws<ArgumentOutOfRangeException>(() => port.Ssid = -1);
    }

    [Fact]
    public void GetPortCallsign_ReturnsCallsignWithSsidWhenSsidIsSet()
    {
        var config = CreateConfiguration();
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        port.Ssid = 7;

        var result = resolver.GetPortCallsign(port, "N0CALL");

        Assert.Equal("N0CALL-7", result);
    }

    [Fact]
    public void GetPortCallsign_ReturnsCallsignWithoutSsidWhenSsidIsNull()
    {
        var config = CreateConfiguration();
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        port.Ssid = null;

        var result = resolver.GetPortCallsign(port, "N0CALL");

        Assert.Equal("N0CALL", result);
    }

    [Fact]
    public void GetPortCallsign_ReturnsCallsignWithoutSsidWhenSsidIsZero()
    {
        var config = CreateConfiguration();
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        port.Ssid = 0;

        var result = resolver.GetPortCallsign(port, "N0CALL");

        Assert.Equal("N0CALL", result);
    }

    [Fact]
    public void GetPortCallsign_UsesDefaultSsidWhenPortSsidIsNull()
    {
        var config = CreateConfiguration();
        config.Settings.Aprs.DefaultSsid = 9;
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        port.Ssid = null;

        var result = resolver.GetPortCallsign(port, "N0CALL");

        Assert.Equal("N0CALL-9", result);
    }

    [Fact]
    public void GetPortBeaconMode_ReturnsPortModeWhenSet()
    {
        var config = CreateConfiguration();
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        port.DynamicBeaconMode = DynamicBeaconMode.Drive;

        var result = resolver.GetPortBeaconMode(port);

        Assert.Equal(DynamicBeaconMode.Drive, result);
    }

    [Fact]
    public void GetPortBeaconMode_ReturnsDefaultModeWhenPortModeIsNull()
    {
        var config = CreateConfiguration();
        config.Settings.Aprs.DefaultBeaconMode = DynamicBeaconMode.Custom;
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        port.DynamicBeaconMode = null;

        var result = resolver.GetPortBeaconMode(port);

        Assert.Equal(DynamicBeaconMode.Custom, result);
    }

    [Fact]
    public void GetPortSymbolTableCharacter_ReturnsPortSymbolWhenSet()
    {
        var config = CreateConfiguration();
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        port.SymbolTableCharacter = "\\";

        var result = resolver.GetPortSymbolTableCharacter(port);

        Assert.Equal("\\", result);
    }

    [Fact]
    public void GetPortSymbolTableCharacter_ReturnsDefaultWhenPortSymbolIsNull()
    {
        var config = CreateConfiguration();
        config.Settings.Aprs.DefaultSymbolTableCharacter = "\\";
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        port.SymbolTableCharacter = null;

        var result = resolver.GetPortSymbolTableCharacter(port);

        Assert.Equal("\\", result);
    }

    [Fact]
    public void GetPortSymbolCodeCharacter_ReturnsPortSymbolWhenSet()
    {
        var config = CreateConfiguration();
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        port.SymbolCodeCharacter = ">";

        var result = resolver.GetPortSymbolCodeCharacter(port);

        Assert.Equal(">", result);
    }

    [Fact]
    public void GetPortSymbolCodeCharacter_ReturnsDefaultWhenPortSymbolIsNull()
    {
        var config = CreateConfiguration();
        config.Settings.Aprs.DefaultSymbolCodeCharacter = "k";
        var resolver = new AprsPortSettingsResolver(config);
        var port = CreatePort();
        port.SymbolCodeCharacter = null;

        var result = resolver.GetPortSymbolCodeCharacter(port);

        Assert.Equal("k", result);
    }

    private static TestConfigurationService CreateConfiguration()
    {
        return new TestConfigurationService();
    }

    private static PortConfig CreatePort()
    {
        return new PortConfig
        {
            Id = Guid.NewGuid(),
            Name = "Test Port",
            Type = PortType.AprsIs,
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

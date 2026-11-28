// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration.Settings;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Ports;

using Xunit;

namespace AetherAprs.Tests.Services.Ports;

public class AprsPortSettingsResolverTests
{
    [Fact]
    public void GetSsid_ReturnsDefaultSsidWhenPositive()
    {
        TestConfigurationService config = CreateConfiguration();
        config.Current = config.Current with { Aprs = config.Current.Aprs with { Ssid = (AprsSsid)3 } };
        AprsPortSettingsResolver resolver = CreateResolver(config);

        Assert.Equal(3, resolver.GetSsid());
    }

    [Fact]
    public void GetSsid_ReturnsNullWhenSsidIsZero()
    {
        TestConfigurationService config = CreateConfiguration();
        AprsPortSettingsResolver resolver = CreateResolver(config);

        Assert.Null(resolver.GetSsid());
    }

    [Fact]
    public void GetCallsign_AppendsSsidWhenPositive()
    {
        TestConfigurationService config = CreateConfiguration();
        config.Current = config.Current with { Aprs = config.Current.Aprs with { Ssid = (AprsSsid)7 } };
        AprsPortSettingsResolver resolver = CreateResolver(config);

        Assert.Equal("N0CALL-7", resolver.GetCallsign("N0CALL"));
    }

    [Fact]
    public void GetCallsign_OmitsSsidWhenZero()
    {
        TestConfigurationService config = CreateConfiguration();
        AprsPortSettingsResolver resolver = CreateResolver(config);

        Assert.Equal("N0CALL", resolver.GetCallsign("N0CALL"));
    }

    [Fact]
    public void GetSymbolTable_ReturnsDefault()
    {
        TestConfigurationService config = CreateConfiguration();
        config.Current = config.Current with { Aprs = config.Current.Aprs with { SymbolTable = AetherAprs.Models.Aprs.SymbolTable.Alternate } };
        AprsPortSettingsResolver resolver = CreateResolver(config);

        Assert.Equal(AetherAprs.Models.Aprs.SymbolTable.Alternate, resolver.GetSymbolTable());
    }

    [Fact]
    public void GetSymbolCode_ReturnsDefault()
    {
        TestConfigurationService config = CreateConfiguration();
        config.Current = config.Current with { Aprs = config.Current.Aprs with { SymbolCode = SymbolCode.LatinSmallLetterK } };
        AprsPortSettingsResolver resolver = CreateResolver(config);

        Assert.Equal(SymbolCode.LatinSmallLetterK, resolver.GetSymbolCode());
    }

    private static TestConfigurationService CreateConfiguration() => new();

    private static AprsPortSettingsResolver CreateResolver(
        IConfigurationService? config = null) => new(config ?? CreateConfiguration());

    private sealed class TestConfigurationService : IConfigurationService
    {
#pragma warning disable CS0067 // Event is never used in this test double
        public event EventHandler? SettingsChanged;
#pragma warning restore CS0067
        public AppSettings Current { get; set; } = new();

        public Task<AppSettings> UpdateAsync(
            Func<AppSettings, AppSettings> update,
            CancellationToken cancellationToken = default)
        {
            Current = update(Current);
            return Task.FromResult(Current);
        }
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Ports;
using AetherAprs.Services.UI;
using NSubstitute;
using Xunit;

namespace AetherAprs.Tests.Services.UI;

public sealed class PacketViewFilterServiceTests
{
    [Fact]
    public void GetCutoffTime_UsesDisplayTimeRange_WhenLoadHistoryOnStartupEnabled()
    {
        var configuration = Substitute.For<IConfigurationService>();
        var settings = new AppSettings
        {
            Aprs = new AprsSettings
            {
                DisplayTimeRange = PacketDisplayTimeRange.LastWeek,
                LoadHistoryOnStartup = true,
            },
        };
        configuration.Current.Returns(settings);

        using var service = new PacketViewFilterService(configuration, Substitute.For<IPortService>());

        var cutoff = service.GetCutoffTime();

        Assert.NotNull(cutoff);
        Assert.InRange(cutoff!.Value, DateTimeOffset.UtcNow.AddDays(-7).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(-7).AddMinutes(1));
    }

    [Fact]
    public void GetCutoffTime_ReturnsNull_WhenTimeRangeIsAllAndHistoryEnabled()
    {
        var configuration = Substitute.For<IConfigurationService>();
        var settings = new AppSettings
        {
            Aprs = new AprsSettings
            {
                DisplayTimeRange = PacketDisplayTimeRange.All,
                LoadHistoryOnStartup = true,
            },
        };
        configuration.Current.Returns(settings);

        using var service = new PacketViewFilterService(configuration, Substitute.For<IPortService>());

        Assert.Null(service.GetCutoffTime());
    }

    [Fact]
    public void GetCutoffTime_ReturnsSessionStart_WhenLoadHistoryOnStartupDisabled()
    {
        var configuration = Substitute.For<IConfigurationService>();
        var settings = new AppSettings
        {
            Aprs = new AprsSettings
            {
                DisplayTimeRange = PacketDisplayTimeRange.All,
                LoadHistoryOnStartup = false,
            },
        };
        configuration.Current.Returns(settings);

        var before = DateTimeOffset.UtcNow;
        using var service = new PacketViewFilterService(configuration, Substitute.For<IPortService>());
        var cutoff = service.GetCutoffTime();

        Assert.NotNull(cutoff);
        Assert.InRange(cutoff!.Value, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void GetCutoffTime_ReturnsSessionStart_WhenSessionStartIsMoreRecentThanRange()
    {
        var configuration = Substitute.For<IConfigurationService>();
        var settings = new AppSettings
        {
            Aprs = new AprsSettings
            {
                DisplayTimeRange = PacketDisplayTimeRange.LastMonth,
                LoadHistoryOnStartup = false,
            },
        };
        configuration.Current.Returns(settings);

        var before = DateTimeOffset.UtcNow;
        using var service = new PacketViewFilterService(configuration, Substitute.For<IPortService>());
        var cutoff = service.GetCutoffTime();

        Assert.NotNull(cutoff);
        Assert.InRange(cutoff!.Value, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void GetVisiblePortIds_ReturnsOnlyPortsShownOnMap()
    {
        var portA = Guid.NewGuid();
        var portB = Guid.NewGuid();

        var portService = Substitute.For<IPortService>();
        portService.Ports.Returns(
        [
            new PortConfig { Id = portA, ShowOnMap = true },
            new PortConfig { Id = portB, ShowOnMap = false }
        ]);

        using var service = new PacketViewFilterService(
            Substitute.For<IConfigurationService>(),
            portService);

        var visible = service.GetVisiblePortIds();

        Assert.Contains(portA, visible);
        Assert.DoesNotContain(portB, visible);
    }

    [Fact]
    public void SettingsChanged_RaisesFiltersChanged()
    {
        var configuration = Substitute.For<IConfigurationService>();
        configuration.Current.Returns(new AppSettings());
        using var service = new PacketViewFilterService(configuration, Substitute.For<IPortService>());

        var raised = 0;
        service.FiltersChanged += (_, _) => raised++;

        configuration.SettingsChanged += Raise.Event<EventHandler>(configuration, EventArgs.Empty);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void PortsChanged_RaisesFiltersChanged()
    {
        var portService = Substitute.For<IPortService>();
        using var service = new PacketViewFilterService(Substitute.For<IConfigurationService>(), portService);

        var raised = 0;
        service.FiltersChanged += (_, _) => raised++;

        portService.PortsChanged += Raise.Event<EventHandler>(portService, EventArgs.Empty);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Dispose_UnsubscribesFromSources()
    {
        var configuration = Substitute.For<IConfigurationService>();
        var portService = Substitute.For<IPortService>();
        var service = new PacketViewFilterService(configuration, portService);

        var raised = 0;
        service.FiltersChanged += (_, _) => raised++;

        service.Dispose();
        configuration.SettingsChanged += Raise.Event<EventHandler>(configuration, EventArgs.Empty);
        portService.PortsChanged += Raise.Event<EventHandler>(portService, EventArgs.Empty);

        Assert.Equal(0, raised);
    }
}

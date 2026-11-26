// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Data.Entities;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Ports;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.Services.UI;

/// <summary>
/// Shared service that manages packet filtering state for both the packets list
/// and map display. Ensures consistent filtering logic across all views.
/// </summary>
public interface IPacketViewFilterService
{
    /// <summary>
    /// Event raised when filters change (time range, port visibility, settings).
    /// Consumers should rebuild their filtered views when this event fires.
    /// </summary>
    event EventHandler? FiltersChanged;

    /// <summary>
    /// Calculates the cutoff time for packet display based on current settings.
    /// Returns null if all packets should be shown (no time filtering).
    /// Respects LoadHistoryOnStartup setting - if false, uses session start time as floor.
    /// </summary>
    DateTimeOffset? GetCutoffTime();

    /// <summary>
    /// Gets the set of port IDs that should be visible on the map based on
    /// current port configuration.
    /// </summary>
    HashSet<Guid> GetVisiblePortIds();
}

/// <summary>
/// Implementation of shared packet filtering logic.
/// </summary>
public sealed class PacketViewFilterService : IPacketViewFilterService, IDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly IPortService _portService;
    private readonly DateTimeOffset _sessionStartedUtc = DateTimeOffset.UtcNow;
    private bool _disposed;

    public event EventHandler? FiltersChanged;

    public PacketViewFilterService(
        IConfigurationService configurationService,
        IPortService portService)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _portService = portService ?? throw new ArgumentNullException(nameof(portService));

        // Subscribe to settings and port changes that affect filtering
        _configurationService.SettingsChanged += OnSettingsChanged;
        _portService.PortsChanged += OnPortsChanged;
    }

    public DateTimeOffset? GetCutoffTime()
    {
        AprsSettings aprs = _configurationService.Current.Aprs;
        DateTimeOffset? cutoffTime = CalculateCutoffTime(aprs.DisplayTimeRange, aprs.CustomDisplayTimeRangeHours);

        // If LoadHistoryOnStartup is false, use session start time as floor
        // This ensures fresh start when the setting is disabled
        if (!aprs.LoadHistoryOnStartup)
        {
            if (!cutoffTime.HasValue || _sessionStartedUtc > cutoffTime.Value)
            {
                cutoffTime = _sessionStartedUtc;
            }
        }

        return cutoffTime;
    }

    public HashSet<Guid> GetVisiblePortIds() => [.. _portService.Ports
            .Where(p => p.ShowOnMap)
            .Select(p => p.Id)];

    private void OnPortsChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            RaiseFiltersChanged();
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            RaiseFiltersChanged();
        }
    }

    private void RaiseFiltersChanged() => FiltersChanged?.Invoke(this, EventArgs.Empty);

    private static DateTimeOffset? CalculateCutoffTime(PacketDisplayTimeRange timeRange, int customHours) => timeRange switch
    {
        PacketDisplayTimeRange.All => null,
        PacketDisplayTimeRange.LastHour => DateTimeOffset.UtcNow.AddHours(-1),
        PacketDisplayTimeRange.LastDay => DateTimeOffset.UtcNow.AddDays(-1),
        PacketDisplayTimeRange.LastWeek => DateTimeOffset.UtcNow.AddDays(-7),
        PacketDisplayTimeRange.LastMonth => DateTimeOffset.UtcNow.AddDays(-30),
        PacketDisplayTimeRange.Custom => DateTimeOffset.UtcNow.AddHours(-customHours),
        _ => DateTimeOffset.UtcNow.AddHours(-24)
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _configurationService.SettingsChanged -= OnSettingsChanged;
        _portService.PortsChanged -= OnPortsChanged;
    }
}

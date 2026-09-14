// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Models;

namespace AetherAprs.Services;

/// <summary>
/// Resolves APRS settings for ports with fallback to global defaults.
/// </summary>
public class AprsPortSettingsResolver : IAprsPortSettingsResolver
{
    private readonly IConfigurationService _configurationService;

    public AprsPortSettingsResolver(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public int? GetPortSsid(PortConfig port)
    {
        var ssid = port.Ssid ?? _configurationService.Settings.Aprs.DefaultSsid;
        return ssid > 0 ? ssid : null;
    }

    public string GetPortCallsign(PortConfig port, string baseCallsign)
    {
        var ssid = GetPortSsid(port);
        return ssid.HasValue ? $"{baseCallsign}-{ssid.Value}" : baseCallsign;
    }

    public DynamicBeaconMode GetPortBeaconMode(PortConfig port) =>
        port.DynamicBeaconMode ?? _configurationService.Settings.Aprs.DefaultBeaconMode;

    public string GetPortSymbolTableCharacter(PortConfig port) =>
        port.SymbolTableCharacter ?? _configurationService.Settings.Aprs.DefaultSymbolTableCharacter;

    public string GetPortSymbolCodeCharacter(PortConfig port) =>
        port.SymbolCodeCharacter ?? _configurationService.Settings.Aprs.DefaultSymbolCodeCharacter;
}

/// <summary>
/// Interface for resolving APRS settings for ports.
/// </summary>
public interface IAprsPortSettingsResolver
{
    /// <summary>
    /// Gets the SSID for a port, falling back to the default SSID from settings.
    /// Returns null if the SSID is 0 or negative.
    /// </summary>
    int? GetPortSsid(PortConfig port);

    /// <summary>
    /// Gets the full callsign for a port (base callsign + SSID if applicable).
    /// </summary>
    string GetPortCallsign(PortConfig port, string baseCallsign);

    /// <summary>
    /// Gets the beacon mode for a port, falling back to the default from settings.
    /// </summary>
    DynamicBeaconMode GetPortBeaconMode(PortConfig port);

    /// <summary>
    /// Gets the symbol table character for a port, falling back to the default from settings.
    /// </summary>
    string GetPortSymbolTableCharacter(PortConfig port);

    /// <summary>
    /// Gets the symbol code character for a port, falling back to the default from settings.
    /// </summary>
    string GetPortSymbolCodeCharacter(PortConfig port);
}

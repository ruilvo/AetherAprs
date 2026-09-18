// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Models;

namespace AetherAprs.Services;

/// <summary>
/// Resolves APRS identity from global settings and per-port beacon mode.
/// </summary>
public class AprsPortSettingsResolver : IAprsPortSettingsResolver
{
    private readonly IConfigurationService _configurationService;
    private readonly IBeaconService _beaconService;

    public AprsPortSettingsResolver(IConfigurationService configurationService, IBeaconService beaconService)
    {
        _configurationService = configurationService;
        _beaconService = beaconService;
    }

    public int? GetSsid()
    {
        var ssid = _configurationService.Settings.Aprs.DefaultSsid;
        return ssid > 0 ? ssid : null;
    }

    public string GetCallsign(string baseCallsign)
    {
        var ssid = GetSsid();
        return ssid.HasValue ? $"{baseCallsign}-{ssid.Value}" : baseCallsign;
    }

    public DynamicBeaconMode GetPortBeaconMode(PortConfig port) =>
        port.DynamicBeaconMode ?? _beaconService.CurrentConfiguration.Mode;

    public string GetSymbolTableCharacter() =>
        _configurationService.Settings.Aprs.DefaultSymbolTableCharacter;

    public string GetSymbolCodeCharacter() =>
        _configurationService.Settings.Aprs.DefaultSymbolCodeCharacter;
}

/// <summary>
/// Interface for resolving APRS settings used when transmitting on ports.
/// </summary>
public interface IAprsPortSettingsResolver
{
    /// <summary>
    /// Gets the configured station SSID.
    /// Returns null if the SSID is 0 or negative.
    /// </summary>
    int? GetSsid();

    /// <summary>
    /// Gets the full station callsign (base callsign + SSID if applicable).
    /// </summary>
    string GetCallsign(string baseCallsign);

    /// <summary>
    /// Gets the beacon mode for a port, falling back to the active dynamic beaconing mode.
    /// </summary>
    DynamicBeaconMode GetPortBeaconMode(PortConfig port);

    /// <summary>
    /// Gets the configured APRS symbol table character.
    /// </summary>
    string GetSymbolTableCharacter();

    /// <summary>
    /// Gets the configured APRS symbol code character.
    /// </summary>
    string GetSymbolCodeCharacter();
}

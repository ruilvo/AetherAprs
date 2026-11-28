// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Services.Configuration;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Resolves APRS identity settings from global configuration.
/// </summary>
public class AprsPortSettingsResolver(IConfigurationService configurationService) : IAprsPortSettingsResolver
{
    private readonly IConfigurationService _configurationService = configurationService;

    public int? GetSsid()
    {
        AprsSsid ssid = _configurationService.Current.Aprs.Ssid;
        int value = (int)ssid;
        return value > 0 ? value : null;
    }

    public string GetCallsign(string baseCallsign)
    {
        int? ssid = GetSsid();
        return ssid.HasValue ? $"{baseCallsign}-{ssid.Value}" : baseCallsign;
    }

    public SymbolTable GetSymbolTable() =>
        _configurationService.Current.Aprs.SymbolTable;

    public SymbolCode GetSymbolCode() =>
        _configurationService.Current.Aprs.SymbolCode;
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
    /// Gets the configured APRS symbol table.
    /// </summary>
    SymbolTable GetSymbolTable();

    /// <summary>
    /// Gets the configured APRS symbol code.
    /// </summary>
    SymbolCode GetSymbolCode();
}

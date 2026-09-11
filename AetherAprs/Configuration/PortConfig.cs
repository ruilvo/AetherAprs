// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using AetherAprs.Models;

namespace AetherAprs.Configuration;

public enum PortType
{
    AprsIs,
    Kiss
}

public class PortConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public PortType Type { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public bool IsRx { get; set; } = true;

    public bool IsTx { get; set; } = false;

    // APRS-IS fields
    public string? Server { get; set; }

    public int ServerPort { get; set; } = 14580;

    public string? Passcode { get; set; }

    public string? Filter { get; set; }

    public int? Ssid { get; set; }

    /// <summary>
    /// APRS symbol table character, normally '/' or '\'.
    /// </summary>
    public string? SymbolTableCharacter { get; set; }

    /// <summary>
    /// APRS symbol code character.
    /// </summary>
    public string? SymbolCodeCharacter { get; set; }

    /// <summary>
    /// Dynamic beaconing mode to use for position transmissions on this port.
    /// Null means that the global APRS default beacon mode is used.
    /// </summary>
    public DynamicBeaconMode? DynamicBeaconMode { get; set; }
}

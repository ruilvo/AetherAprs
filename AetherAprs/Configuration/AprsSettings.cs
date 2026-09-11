// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;

namespace AetherAprs.Configuration;

public class AprsSettings
{
    public string Callsign { get; set; } = "N0CALL";

    public int DefaultSsid { get; set; }

    public string DefaultSymbolTableCharacter { get; set; } = "/";

    public string DefaultSymbolCodeCharacter { get; set; } = "[";

    public DynamicBeaconMode DefaultBeaconMode { get; set; } = DynamicBeaconMode.Walk;
}

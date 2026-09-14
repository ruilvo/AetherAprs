// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Configuration;

namespace AetherAprs.Extensions;

/// <summary>
/// Extension methods for working with PortConfig and type-specific settings.
/// </summary>
public static class PortConfigExtensions
{
    /// <summary>
    /// Gets the APRS-IS settings for this port, or null if not an APRS-IS port.
    /// </summary>
    public static AprsIsSettings? GetAprsIsSettings(this PortConfig port)
    {
        return port.TypeSettings as AprsIsSettings;
    }

    /// <summary>
    /// Gets the APRS-IS settings for this port, throwing if not an APRS-IS port.
    /// </summary>
    public static AprsIsSettings GetAprsIsSettingsOrThrow(this PortConfig port)
    {
        if (port.TypeSettings is not AprsIsSettings settings)
        {
            throw new InvalidOperationException($"Port {port.Name} is not an APRS-IS port");
        }
        return settings;
    }

    /// <summary>
    /// Ensures the port has APRS-IS settings, creating default settings if needed.
    /// </summary>
    public static AprsIsSettings EnsureAprsIsSettings(this PortConfig port)
    {
        if (port.TypeSettings is not AprsIsSettings settings)
        {
            settings = new AprsIsSettings();
            port.TypeSettings = settings;
        }
        return settings;
    }

    /// <summary>
    /// Gets the KISS settings for this port, or null if not a KISS port.
    /// </summary>
    public static KissSettings? GetKissSettings(this PortConfig port)
    {
        return port.TypeSettings as KissSettings;
    }
}

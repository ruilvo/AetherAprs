// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Models.Aprs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace AetherAprs.Services;

/// <summary>
/// Implements digipeater functionality for APRS packets.
/// </summary>
public class DigipeaterService : IDigipeaterService
{
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<DigipeaterService> _logger;

    public DigipeaterService(
        IConfigurationService configurationService,
        ILogger<DigipeaterService> logger)
    {
        _configurationService = configurationService;
        _logger = logger;
    }

    public IReadOnlyList<DigipeatTarget> GetDigipeatTargets(
        AprsPacket packet,
        Guid sourcePortId,
        bool sourcePortIsAprsIs,
        IReadOnlyList<PortInfo> availablePorts)
    {
        var settings = _configurationService.Settings.Aprs;

        // Check if digipeater is globally enabled
        if (!settings.EnableDigipeater)
        {
            return Array.Empty<DigipeatTarget>();
        }

        var targets = new List<DigipeatTarget>();

        // Determine gating rules
        bool isRfToAprsIs = !sourcePortIsAprsIs;
        bool isAprsIsToRf = sourcePortIsAprsIs;

        foreach (var port in availablePorts)
        {
            // Skip source port
            if (port.Id == sourcePortId)
            {
                continue;
            }

            // Skip ports that don't have TX enabled or don't allow digipeat
            if (!port.IsTxEnabled || !port.AllowDigipeat)
            {
                continue;
            }

            // Check gating rules
            if (isAprsIsToRf && port.IsAprsIs)
            {
                // APRS-IS to APRS-IS: never forward (APRS-IS servers handle distribution)
                continue;
            }

            if (isAprsIsToRf && !port.IsAprsIs)
            {
                // APRS-IS to RF: only if enabled
                if (!settings.EnableAprsIsToRfGate)
                {
                    continue;
                }
            }

            if (isRfToAprsIs && port.IsAprsIs)
            {
                // RF to APRS-IS: only if enabled
                if (!settings.EnableRfToAprsIsGate)
                {
                    continue;
                }
            }

            // For RF to RF digipeating, we should add the station callsign to the path
            // For now, we'll forward the packet as-is since AprsPacket doesn't have path fields
            // In a full implementation, we would:
            // 1. Parse the path from the destination field
            // 2. Check if we should digipeat (WIDE1-1, WIDE2-1, etc.)
            // 3. Decrement hop counts and add our callsign
            // 4. Create a modified packet with the updated path

            targets.Add(new DigipeatTarget(port.Id, packet));

            _logger.LogDebug(
                "Digipeating packet from {Source} (port {SourcePortId}) to port {TargetPortId}",
                packet.Source,
                sourcePortId,
                port.Id);
        }

        return targets;
    }
}

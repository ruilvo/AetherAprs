// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using System;
using System.Collections.Generic;

namespace AetherAprs.Services;

/// <summary>
/// Service for digipeating APRS packets between ports.
/// </summary>
public interface IDigipeaterService
{
    /// <summary>
    /// Processes a received packet and determines which ports it should be digipeated to.
    /// </summary>
    /// <param name="packet">The received packet.</param>
    /// <param name="sourcePortId">The ID of the port that received the packet.</param>
    /// <param name="sourcePortIsAprsIs">True if the source port is an APRS-IS port.</param>
    /// <param name="availablePorts">Information about available ports for digipeating.</param>
    /// <returns>List of ports to digipeat to with potentially modified packets.</returns>
    IReadOnlyList<DigipeatTarget> GetDigipeatTargets(
        AprsPacket packet,
        Guid sourcePortId,
        bool sourcePortIsAprsIs,
        IReadOnlyList<PortInfo> availablePorts);
}

/// <summary>
/// Information about a port for digipeating decisions.
/// </summary>
public record PortInfo(
    Guid Id,
    bool IsAprsIs,
    bool IsTxEnabled,
    bool AllowDigipeat);

/// <summary>
/// Represents a target port for digipeating with a potentially modified packet.
/// </summary>
public record DigipeatTarget(
    Guid PortId,
    AprsPacket Packet);

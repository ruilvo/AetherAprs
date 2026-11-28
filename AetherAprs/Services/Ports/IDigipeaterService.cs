// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

using AetherAprs.Models.Aprs.Packets;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Forwarding rate limit applied per source port. A source that exceeds the limit in
/// the window has its candidate rejected instead of amplified onto other ports.
/// </summary>
public sealed record DigipeatRateLimitOptions
{
    /// <summary>Rate limit used when the caller does not override it.</summary>
    public static DigipeatRateLimitOptions Default { get; } = new();

    /// <summary>Maximum forwards accepted from one source port within the window.</summary>
    public int MaxForwardsPerWindow { get; init; } = 60;

    /// <summary>Fixed window over which forwards are counted.</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// One forwarding decision for a target port, or a packet-level decision when
/// <paramref name="TargetPortId"/> is <c>null</c>. The reason is the accept/reject
/// verdict; the payload is never part of the decision.
/// </summary>
/// <param name="TargetPortId">The target port, or <c>null</c> for a packet-level decision.</param>
/// <param name="Reason">The accept or reject reason.</param>
public readonly record struct DigipeatDecision(Guid? TargetPortId, DigipeatReason Reason);

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

    /// <summary>
    /// Evaluates the same forwarding decision as <see cref="GetDigipeatTargets"/> and
    /// returns every accept/reject reason, including the ports that were gated out.
    /// Intended for diagnostics and golden-policy tests; the router uses the target list.
    /// Each call consumes the packet's place in the duplicate and rate-limit windows,
    /// so a caller must not evaluate the same packet twice.
    /// </summary>
    /// <param name="packet">The received packet.</param>
    /// <param name="sourcePortId">The ID of the port that received the packet.</param>
    /// <param name="sourcePortIsAprsIs">True if the source port is an APRS-IS port.</param>
    /// <param name="availablePorts">Information about available ports for digipeating.</param>
    /// <returns>One decision per gated port plus packet-level rejects.</returns>
    IReadOnlyList<DigipeatDecision> Evaluate(
        AprsPacket packet,
        Guid sourcePortId,
        bool sourcePortIsAprsIs,
        IReadOnlyList<PortInfo> availablePorts);

    /// <summary>Gets the bounded route/reason history, oldest first.</summary>
    /// <param name="maximum">Maximum number of records to return.</param>
    IReadOnlyList<DigipeatDecisionRecord> GetRecentDecisions(int maximum = DigipeatDecisionLog.DefaultCapacity);

    /// <summary>Gets how many decisions were recorded with the given reason.</summary>
    /// <param name="reason">The accept or reject reason.</param>
    long GetDecisionCount(DigipeatReason reason);
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

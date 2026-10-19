// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration.Settings;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Implements digipeater functionality for APRS packets.
/// </summary>
public class DigipeaterService(
    IConfigurationService configurationService,
    ILogger<DigipeaterService> logger) : IDigipeaterService
{
    /// <summary>
    /// Maximum number of recently digipeated packets tracked for duplicate suppression.
    /// </summary>
    private const int DedupTableCapacity = 256;

    /// <summary>
    /// How long a packet stays in the dedup table before it may be digipeated again.
    /// </summary>
    private static readonly TimeSpan DedupRetention = TimeSpan.FromSeconds(30);

    private readonly object _dedupLock = new();
    private readonly Queue<(string Key, DateTimeOffset Seen)> _dedupOrder = new();
    private readonly Dictionary<string, DateTimeOffset> _dedupTable = new();

    /// <inheritdoc />
    public IReadOnlyList<DigipeatTarget> GetDigipeatTargets(
        AprsPacket packet,
        Guid sourcePortId,
        bool sourcePortIsAprsIs,
        IReadOnlyList<PortInfo> availablePorts)
    {
        var settings = configurationService.Settings.Aprs;

        // Check if digipeater is globally enabled
        if (!settings.EnableDigipeater)
        {
            return [];
        }

        // Only forward packet types we can re-serialize for transmission; the
        // APRS serializer throws for telemetry/capabilities payloads, which would
        // make every target send fail (silently logged) downstream.
        if (!IsDigipeatable(packet))
        {
            return [];
        }

        // Never digipeat packets that originated from our own station: when our own
        // digipeated transmission is heard again by our TNC (normal APRS behavior),
        // re-digipeating it would create an unbounded RF loop.
        var ownCallsign = BuildOwnCallsign(settings);
        if (PacketIsFromOwnStation(packet, ownCallsign))
        {
            return [];
        }

        // Duplicate suppression: the same packet (source+destination+info) heard again
        // within the retention window must not be digipeated twice.
        if (IsDuplicate(packet))
        {
            logger.LogDebug(
                "Suppressing duplicate packet from {Source} for digipeating.",
                packet.Source);
            return [];
        }

        var targets = new List<DigipeatTarget>();

        // Determine gating rules
        bool isRfToAprsIs = !sourcePortIsAprsIs;
        bool isAprsIsToRf = sourcePortIsAprsIs;

        // For RF-sourced packets, compute the forwarded path once: find the first
        // untraced WIDE hop, decrement it, and insert our callsign. When true,
        // suppressRfForwarding means the packet must not be digipeated on RF
        // (hops exhausted).
        bool suppressRfForwarding = false;
        IReadOnlyList<Callsign> forwardedPath = Array.Empty<Callsign>();
        if (isRfToAprsIs)
        {
            forwardedPath = BuildForwardedPath(packet, ownCallsign, out suppressRfForwarding);
        }

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

            if (isRfToAprsIs && !port.IsAprsIs)
            {
                // RF to RF: only forward if there is still an untraced hop to service.
                if (suppressRfForwarding)
                {
                    continue;
                }

                targets.Add(new DigipeatTarget(port.Id, packet with { Path = forwardedPath }));
                logger.LogDebug(
                    "Digipeating packet from {Source} (port {SourcePortId}) to port {TargetPortId} with path {Path}",
                    packet.Source,
                    sourcePortId,
                    port.Id,
                    PathHelper.FormatPath(forwardedPath));
                continue;
            }

            targets.Add(new DigipeatTarget(port.Id, packet));
            logger.LogDebug(
                "Digipeating packet from {Source} (port {SourcePortId}) to port {TargetPortId}",
                packet.Source,
                sourcePortId,
                port.Id);
        }

        return targets;
    }

    /// <summary>
    /// Determines whether the packet's info field can be re-serialized for transmission.
    /// Telemetry and capability payloads have no APRS serializer support and cannot be
    /// forwarded.
    /// </summary>
    private static bool IsDigipeatable(AprsPacket packet) => packet switch
    {
        PositionPacket or MessagePacket or MessageAckPacket or MessageRejPacket
            or StatusPacket or WeatherPacket or UnknownPacket => true,
        _ => false
    };

    /// <summary>
    /// Builds our own full callsign (base + SSID) for own-station detection.
    /// Returns <c>null</c> when no well-formed callsign is configured.
    /// </summary>
    private static Callsign? BuildOwnCallsign(AprsSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Callsign))
        {
            return null;
        }

        if (!Callsign.TryParse(settings.Callsign, out var callsign))
        {
            return null;
        }

        var ssid = (int)settings.Ssid;
        return ssid > 0 ? new Callsign(callsign.Base, ssid) : callsign;
    }

    /// <summary>
    /// Determines whether the packet was originated by our own station.
    /// </summary>
    private static bool PacketIsFromOwnStation(AprsPacket packet, Callsign? ownCallsign)
    {
        if (ownCallsign is null)
        {
            return false;
        }

        if (packet.Source == ownCallsign.Value)
        {
            return true;
        }

        // Some packets keep the original source only as a raw identifier
        // (non-standard addressees) — compare it too.
        return packet.RawSource is not null
            && string.Equals(packet.RawSource, ownCallsign.Value.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Records the packet in the dedup table and returns whether it was already seen
    /// within the retention window.
    /// </summary>
    private bool IsDuplicate(AprsPacket packet)
    {
        var key = $"{packet.Source}|{packet.Destination}|{packet.Raw}";
        var now = DateTimeOffset.UtcNow;

        lock (_dedupLock)
        {
            if (_dedupTable.TryGetValue(key, out var seen) && now - seen < DedupRetention)
            {
                // Refresh so a frequently repeated packet does not cycle back in.
                _dedupTable[key] = now;
                return true;
            }

            _dedupTable[key] = now;
            _dedupOrder.Enqueue((key, now));

            // Bounded LRU: evict expired entries first, then by capacity.
            while (_dedupOrder.Count > 0
                && (now - _dedupOrder.Peek().Seen >= DedupRetention || _dedupOrder.Count > DedupTableCapacity))
            {
                var oldest = _dedupOrder.Dequeue();
                _dedupTable.Remove(oldest.Key);
            }

            return false;
        }
    }

    /// <summary>
    /// Computes the forwarded path for RF digipeating: inserts our callsign into the
    /// path and decrements the first untraced WIDE/TRACE hop.
    /// </summary>
    /// <param name="packet">The received packet.</param>
    /// <param name="ownCallsign">Our own callsign, or <c>null</c> when none is configured.</param>
    /// <param name="suppressForwarding">
    /// Set to <c>true</c> when the packet must not be forwarded on RF at all
    /// (a hop was consumed without remaining hops to service).
    /// </param>
    /// <returns>The path to transmit the packet with.</returns>
    private static IReadOnlyList<Callsign> BuildForwardedPath(
        AprsPacket packet,
        Callsign? ownCallsign,
        out bool suppressForwarding)
    {
        suppressForwarding = false;

        // With no own callsign configured we cannot insert ourselves into the path;
        // forward without modification rather than corrupting the path.
        if (ownCallsign is null)
        {
            return packet.Path;
        }

        // Packets that arrived without a parsed path (e.g. some IS sources): just
        // mark our retransmission by prepending our callsign.
        if (packet.Path.Count == 0)
        {
            return [ownCallsign.Value];
        }

        var forwarded = new List<Callsign>(packet.Path.Count + 1);
        var inserted = false;

        foreach (var hop in packet.Path)
        {
            if (!inserted)
            {
                forwarded.Add(ownCallsign.Value);
                inserted = true;
            }

            var isWideOrTrace = hop.Base is "WIDE" or "TRACE";
            if (isWideOrTrace && hop.Ssid is > 0)
            {
                // First untraced WIDE/TRACE hop: decrement it and stop modifying.
                forwarded.Add(new Callsign(hop.Base, hop.Ssid.Value - 1));
                return forwarded;
            }

            if (isWideOrTrace)
            {
                // A WIDE/TRACE hop with no remaining hops: this packet has already
                // been serviced — it must not travel further via us.
                suppressForwarding = true;
                return forwarded;
            }

            forwarded.Add(hop);
        }

        // Path contained no decremented hop (e.g. only explicit digipeaters):
        // forward with our insertion only.
        return forwarded;
    }
}

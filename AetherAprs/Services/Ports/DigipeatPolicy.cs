// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using System;
using System.Collections.Generic;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Why a forwarding candidate was accepted or rejected. The reason travels with the
/// decision so the router, counters and history can report forwarding outcomes
/// without ever inspecting or logging the packet payload.
/// </summary>
public enum DigipeatReason
{
    /// <summary>The candidate was accepted for the target port.</summary>
    Accepted,

    /// <summary>The operator has not enabled the digipeater.</summary>
    DigipeaterDisabled,

    /// <summary>The packet type has no serializer support and cannot be forwarded.</summary>
    PacketTypeNotForwardable,

    /// <summary>The packet was originated by this station.</summary>
    OwnStationSource,

    /// <summary>The path already names this station, so forwarding it would loop.</summary>
    LoopDetected,

    /// <summary>The same packet was already heard inside the scoped duplicate window.</summary>
    DuplicateSuppressed,

    /// <summary>The source port exceeded the forwarding rate limit for this window.</summary>
    RateLimited,

    /// <summary>The source port is no longer configured.</summary>
    SourcePortUnknown,

    /// <summary>The target port is the port that received the packet.</summary>
    SourcePortIsTarget,

    /// <summary>The target port cannot transmit.</summary>
    TargetNotTransmitCapable,

    /// <summary>The target port is not marked as participating in digipeating.</summary>
    TargetDoesNotAllowDigipeat,

    /// <summary>APRS-IS to RF gating is disabled.</summary>
    InternetToRfDisabled,

    /// <summary>APRS-IS to APRS-IS is never forwarded; the servers distribute packets.</summary>
    InternetToInternetNotForwarded,

    /// <summary>The APRS-IS source is not on the Internet-to-RF allowlist.</summary>
    InternetToRfSourceNotAllowed,

    /// <summary>RF to APRS-IS gating is disabled.</summary>
    RfToInternetDisabled,

    /// <summary>No other port was available to receive the forward.</summary>
    NoTargetPorts,

    /// <summary>The path has no unserviced alias this station could act on.</summary>
    NoServiceableAlias,

    /// <summary>The first unserviced alias is one this station is not configured to serve.</summary>
    AliasNotEnabled,

    /// <summary>The first unserviced alias has no remaining hops.</summary>
    HopsExhausted,

    /// <summary>The target port is not running, so no forward may be sent.</summary>
    TargetNotRunning,

    /// <summary>The candidate aged out before it could be transmitted.</summary>
    CandidateExpired,

    /// <summary>The transmission to the target port failed.</summary>
    TransmitFailed,

    /// <summary>The forwarding queue was full, so the incoming candidate was dropped.</summary>
    QueueFull,
}

/// <summary>
/// Which digipeater aliases this station serves and whether it marks its retransmission
/// with its own callsign. Kept as a value object so path mutation stays pure.
/// </summary>
public sealed record DigipeatAliasPolicy
{
    /// <summary>Policy matching the persisted defaults: WIDE2 served, station callsign inserted.</summary>
    public static DigipeatAliasPolicy Default { get; } = new();

    /// <summary>Gets whether WIDE1-N (fill-in) hops are served. Default is false.</summary>
    public bool RespondToWide1 { get; init; }

    /// <summary>Gets whether WIDE2-N (full) hops are served. Default is true.</summary>
    public bool RespondToWide2 { get; init; } = true;

    /// <summary>Gets whether TRACEn-N hops are served. Default is false.</summary>
    public bool RespondToTrace { get; init; }

    /// <summary>Gets whether this station inserts its own callsign when it repeats. Default is true.</summary>
    public bool InsertCallsign { get; init; } = true;
}

/// <summary>
/// The result of mutating a received path for retransmission. <see cref="Path"/> keeps
/// every original hop in order (provenance) and marks serviced hops as repeated;
/// <see cref="Reason"/> is <see cref="DigipeatReason.Accepted"/> only when the path may
/// be retransmitted by this station.
/// </summary>
public sealed record DigipeatPathDecision(IReadOnlyList<Callsign> Path, DigipeatReason Reason);

/// <summary>
/// Pure forwarding policy. Every method is deterministic and free of I/O, configuration
/// lookups, clocks and mutation, so the same packet, path and alias policy always yields
/// the same decision and the golden tests can assert exact reasons.
/// </summary>
public static class DigipeatPolicy
{
    private static readonly char[] AllowlistSeparators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>
    /// Determines whether the packet's info field can be re-serialized for
    /// transmission. Telemetry and capability payloads have no APRS serializer
    /// support and cannot be forwarded.
    /// </summary>
    public static bool IsDigipeatable(AprsPacket packet) => packet switch
    {
        PositionPacket or ObjectPacket or ItemPacket or MessagePacket or MessageAckPacket
            or MessageRejPacket or BulletinPacket or StatusPacket or WeatherPacket
            or ThirdPartyPacket or QueryPacket or RawGpsPacket or UnknownPacket => true,
        _ => false
    };

    /// <summary>Determines whether a path hop is a WIDE1, WIDE2 or TRACE digipeater alias.</summary>
    public static bool IsDigipeaterAlias(Callsign hop) =>
        hop.Base is "WIDE1" or "WIDE2" or "TRACE";

    /// <summary>Determines whether this station is configured to serve the given alias.</summary>
    public static bool RespondsToAlias(Callsign hop, DigipeatAliasPolicy policy) => hop.Base switch
    {
        "WIDE1" => policy.RespondToWide1,
        "WIDE2" => policy.RespondToWide2,
        "TRACE" => policy.RespondToTrace,
        _ => false
    };

    /// <summary>
    /// Determines whether the path shows that this station has already repeated the
    /// frame, which means forwarding it again would create an RF loop. Only a hop that
    /// names this station <em>and</em> carries the repeated bit counts: an un-repeated
    /// hop naming this station is an explicit request to repeat it once. SSID 0 and no
    /// SSID are treated as the same station.
    /// </summary>
    public static bool IsLoop(IReadOnlyList<Callsign> path, Callsign? ownCallsign)
    {
        if (ownCallsign is null)
        {
            return false;
        }

        foreach (Callsign hop in path)
        {
            if (hop.HasBeenRepeated && IsSameStation(hop, ownCallsign.Value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Computes the retransmission path from a received path. Already repeated hops are
    /// preserved untouched; the first unserviced hop decides the outcome. Serving an
    /// alias inserts this station (when enabled) and decrements the alias, both marked
    /// as repeated. A hop that names this station explicitly is marked repeated in
    /// place. Every hop after the serviced one is carried over unchanged.
    /// </summary>
    /// <param name="path">The received path.</param>
    /// <param name="ownCallsign">This station's callsign, or <c>null</c> when none is configured.</param>
    /// <param name="policy">The aliases this station serves.</param>
    /// <returns>The decision, including the reject reason when the path may not be relayed.</returns>
    public static DigipeatPathDecision EvaluatePath(
        IReadOnlyList<Callsign> path,
        Callsign? ownCallsign,
        DigipeatAliasPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(policy);

        // A path that already shows our repeated hop is a loop regardless of what
        // unserviced hops remain after it.
        if (IsLoop(path, ownCallsign))
        {
            return new DigipeatPathDecision([.. path], DigipeatReason.LoopDetected);
        }

        // Without our own callsign we cannot mark a retransmission, so the path is
        // forwarded unchanged rather than corrupted.
        if (ownCallsign is null)
        {
            return new DigipeatPathDecision([.. path], DigipeatReason.Accepted);
        }

        // Packets that arrived without a parsed path are marked by prepending our
        // callsign, which also records the provenance of the retransmission.
        if (path.Count == 0)
        {
            return new DigipeatPathDecision(
                [new Callsign(ownCallsign.Value.Base, ownCallsign.Value.Ssid, hasBeenRepeated: true)],
                DigipeatReason.Accepted);
        }

        List<Callsign> forwarded = new(path.Count + 1);

        for (int index = 0; index < path.Count; index++)
        {
            Callsign hop = path[index];

            // A hop already marked "has been repeated" was serviced upstream. Preserve
            // it and keep scanning for the first unserviced hop we are responsible for.
            if (hop.HasBeenRepeated)
            {
                forwarded.Add(hop);
                continue;
            }

            // An un-repeated hop naming this station is an explicit request to repeat
            // the frame through us: mark that hop serviced and leave the rest of the
            // path exactly as received.
            if (!IsDigipeaterAlias(hop) && IsSameStation(hop, ownCallsign.Value))
            {
                forwarded.Add(new Callsign(hop.Base, hop.Ssid, hasBeenRepeated: true));
                AppendRemaining(path, index + 1, forwarded);
                return new DigipeatPathDecision(forwarded, DigipeatReason.Accepted);
            }

            // Other explicit digipeater callsigns are not ours to service; preserve
            // them and keep scanning for the first alias we are responsible for.
            if (!IsDigipeaterAlias(hop))
            {
                forwarded.Add(hop);
                continue;
            }

            // The first unserviced alias decides whether this station forwards at all.
            if (hop.Ssid is > 0)
            {
                if (!RespondsToAlias(hop, policy))
                {
                    return new DigipeatPathDecision([.. forwarded], DigipeatReason.AliasNotEnabled);
                }

                if (policy.InsertCallsign || hop.Base == "TRACE")
                {
                    forwarded.Add(new Callsign(ownCallsign.Value.Base, ownCallsign.Value.Ssid, hasBeenRepeated: true));
                }

                forwarded.Add(new Callsign(hop.Base, hop.Ssid.Value - 1, hasBeenRepeated: true));
                AppendRemaining(path, index + 1, forwarded);
                return new DigipeatPathDecision(forwarded, DigipeatReason.Accepted);
            }

            // An alias with no remaining hops was already exhausted upstream.
            return new DigipeatPathDecision([.. forwarded], DigipeatReason.HopsExhausted);
        }

        // Only repeated hops or explicit digipeater callsigns: no alias remains for us.
        return new DigipeatPathDecision([.. forwarded], DigipeatReason.NoServiceableAlias);
    }

    /// <summary>
    /// Parses the persisted Internet-to-RF allowlist. Entries are comma, semicolon or
    /// whitespace separated callsigns; malformed entries are ignored so a bad edit can
    /// never widen the gate. An empty result means "no restriction" and is treated as
    /// allow-all by <see cref="IsInternetToRfSourceAllowed"/>.
    /// </summary>
    public static IReadOnlyList<Callsign> ParseAllowlist(string? allowlist)
    {
        if (string.IsNullOrWhiteSpace(allowlist))
        {
            return [];
        }

        List<Callsign> entries = [];
        foreach (string entry in allowlist.Split(AllowlistSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Callsign.TryParse(entry, out Callsign callsign))
            {
                entries.Add(callsign);
            }
        }

        return entries;
    }

    /// <summary>
    /// Determines whether an APRS-IS source may be gated to RF. An entry without an
    /// SSID matches every SSID of that base; an entry with an SSID must match exactly.
    /// An empty allowlist allows every source that passed the other gate checks.
    /// </summary>
    public static bool IsInternetToRfSourceAllowed(Callsign source, IReadOnlyList<Callsign> allowlist)
    {
        ArgumentNullException.ThrowIfNull(allowlist);

        if (allowlist.Count == 0)
        {
            return true;
        }

        foreach (Callsign entry in allowlist)
        {
            if (entry.Ssid is null)
            {
                if (string.Equals(entry.Base, source.Base, StringComparison.Ordinal))
                {
                    return true;
                }

                continue;
            }

            if (IsSameStation(entry, source))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSameStation(Callsign left, Callsign right) =>
        string.Equals(left.Base, right.Base, StringComparison.Ordinal)
        && (left.Ssid ?? 0) == (right.Ssid ?? 0);

    private static void AppendRemaining(IReadOnlyList<Callsign> path, int start, List<Callsign> destination)
    {
        for (int index = start; index < path.Count; index++)
        {
            destination.Add(path[index]);
        }
    }
}

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
using System.Threading;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Applies the forwarding policy: global enablement, packet eligibility, loop and
/// duplicate checks, per-direction gates, per-port participation and the rate limit.
/// Path mutation itself lives in <see cref="DigipeatPolicy"/>; this type only binds the
/// policy to configuration, the clock and the bounded decision history.
/// </summary>
public class DigipeaterService : IDigipeaterService
{
    /// <summary>
    /// Maximum number of recently heard packets tracked for duplicate suppression.
    /// </summary>
    private const int DedupTableCapacity = 256;

    /// <summary>
    /// How long a packet stays in the heard table before it may be digipeated again.
    /// </summary>
    private static readonly TimeSpan DedupRetention = TimeSpan.FromSeconds(30);

    private readonly IConfigurationService _configurationService;
    private readonly ILogger<DigipeaterService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly DigipeatDecisionLog _decisionLog;
    private readonly DigipeatRateLimiter _rateLimiter;
    private readonly Lock _dedupLock = new();
    private readonly Queue<(int Key, DateTimeOffset Seen)> _dedupOrder = new();
    private readonly Dictionary<int, DateTimeOffset> _dedupTable = [];

    /// <summary>Initializes a new instance of <see cref="DigipeaterService"/>.</summary>
    /// <param name="configurationService">Provides the current operator settings.</param>
    /// <param name="logger">Receives structured forwarding diagnostics.</param>
    /// <param name="timeProvider">Clock used for duplicate and rate-limit windows.</param>
    /// <param name="rateLimitOptions">Per-source-port forwarding rate limit.</param>
    /// <param name="decisionLog">Bounded counters and route/reason history, shared with the router.</param>
    public DigipeaterService(
        IConfigurationService configurationService,
        ILogger<DigipeaterService> logger,
        TimeProvider? timeProvider = null,
        DigipeatRateLimitOptions? rateLimitOptions = null,
        DigipeatDecisionLog? decisionLog = null)
    {
        ArgumentNullException.ThrowIfNull(configurationService);
        ArgumentNullException.ThrowIfNull(logger);

        _configurationService = configurationService;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _decisionLog = decisionLog ?? new DigipeatDecisionLog();
        _rateLimiter = new DigipeatRateLimiter(rateLimitOptions ?? DigipeatRateLimitOptions.Default);
    }

    /// <inheritdoc />
    public IReadOnlyList<DigipeatTarget> GetDigipeatTargets(
        AprsPacket packet,
        Guid sourcePortId,
        bool sourcePortIsAprsIs,
        IReadOnlyList<PortInfo> availablePorts) =>
        EvaluateInternal(packet, sourcePortId, sourcePortIsAprsIs, availablePorts).Targets;

    /// <inheritdoc />
    public IReadOnlyList<DigipeatDecision> Evaluate(
        AprsPacket packet,
        Guid sourcePortId,
        bool sourcePortIsAprsIs,
        IReadOnlyList<PortInfo> availablePorts) =>
        EvaluateInternal(packet, sourcePortId, sourcePortIsAprsIs, availablePorts).Decisions;

    /// <inheritdoc />
    public IReadOnlyList<DigipeatDecisionRecord> GetRecentDecisions(int maximum = DigipeatDecisionLog.DefaultCapacity) =>
        _decisionLog.GetRecentDecisions(maximum);

    /// <inheritdoc />
    public long GetDecisionCount(DigipeatReason reason) => _decisionLog.GetCount(reason);

    private (IReadOnlyList<DigipeatTarget> Targets, IReadOnlyList<DigipeatDecision> Decisions) EvaluateInternal(
        AprsPacket packet,
        Guid sourcePortId,
        bool sourcePortIsAprsIs,
        IReadOnlyList<PortInfo> availablePorts)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(availablePorts);

        AprsSettings settings = _configurationService.Current.Aprs;
        DateTimeOffset now = _timeProvider.GetUtcNow();

        DigipeatReason? packetLevelReject = FirstPacketLevelReject(packet, settings, out Callsign? ownCallsign);
        if (packetLevelReject is DigipeatReason reject)
        {
            return Reject(packetLevelReject: reject, sourcePortId, now);
        }

        DigipeatAliasPolicy aliasPolicy = new()
        {
            RespondToWide1 = settings.DigipeaterRespondToWide1,
            RespondToWide2 = settings.DigipeaterRespondToWide2,
            RespondToTrace = settings.DigipeaterRespondToTrace,
            InsertCallsign = settings.DigipeaterInsertCallsign,
        };

        // Only RF-to-RF retransmission mutates the path. RF-to-APRS-IS keeps the packet
        // as heard, and APRS-IS-to-RF has no RF path to service.
        DigipeatPathDecision pathDecision = sourcePortIsAprsIs
            ? new DigipeatPathDecision([.. packet.Path], DigipeatReason.Accepted)
            : DigipeatPolicy.EvaluatePath(packet.Path, ownCallsign, aliasPolicy);

        List<Callsign> allowlist = [.. DigipeatPolicy.ParseAllowlist(settings.AprsIsToRfAllowlist)];
        bool isAprsIsToRf = sourcePortIsAprsIs;

        List<DigipeatTarget> targets = [];
        List<DigipeatDecision> decisions = [];

        foreach (PortInfo port in availablePorts)
        {
            DigipeatReason? rejectReason = EvaluatePort(
                packet,
                port,
                sourcePortId,
                isAprsIsToRf,
                settings,
                allowlist,
                pathDecision,
                out AprsPacket? targetPacket);

            if (rejectReason is DigipeatReason portReject)
            {
                decisions.Add(new DigipeatDecision(port.Id, portReject));
                continue;
            }

            targets.Add(new DigipeatTarget(port.Id, targetPacket!));
            decisions.Add(new DigipeatDecision(port.Id, DigipeatReason.Accepted));
        }

        // The rate limit counts accepted candidates, not gated ports: one received
        // frame consumes one slot no matter how many ports it would reach.
        if (targets.Count > 0 && !_rateLimiter.TryConsume(sourcePortId, now))
        {
            _logger.LogWarning(
                "Forwarding rate limit reached for source port {SourcePortId}; candidate dropped.",
                sourcePortId);
            return Reject(packetLevelReject: DigipeatReason.RateLimited, sourcePortId, now);
        }

        if (decisions.Count == 0)
        {
            // The port set held only the source port, so no route existed to evaluate.
            decisions.Add(new DigipeatDecision(TargetPortId: null, DigipeatReason.NoTargetPorts));
        }

        Record(decisions, sourcePortId, now);

        foreach (DigipeatTarget target in targets)
        {
            _logger.LogDebug(
                "Digipeating packet from {Source} (port {SourcePortId}) to port {TargetPortId}",
                packet.Source,
                sourcePortId,
                target.PortId);
        }

        return (targets, decisions);
    }

    private DigipeatReason? FirstPacketLevelReject(AprsPacket packet, AprsSettings settings, out Callsign? ownCallsign)
    {
        ownCallsign = BuildOwnCallsign(settings);

        if (!settings.EnableDigipeater)
        {
            return DigipeatReason.DigipeaterDisabled;
        }

        if (!DigipeatPolicy.IsDigipeatable(packet))
        {
            return DigipeatReason.PacketTypeNotForwardable;
        }

        // Never forward packets that originated from our own station: when our own
        // digipeated transmission is heard again by our TNC (normal APRS behaviour),
        // re-digipeating it would create an unbounded RF loop.
        if (PacketIsFromOwnStation(packet, ownCallsign))
        {
            return DigipeatReason.OwnStationSource;
        }

        // A path that already shows our repeated hop means the frame has passed through
        // this station; repeating it again would loop even though the duplicate window
        // may have lapsed.
        if (DigipeatPolicy.IsLoop(packet.Path, ownCallsign))
        {
            return DigipeatReason.LoopDetected;
        }

        // Duplicate suppression: the same packet (source+destination+info) heard again
        // within the retention window must not be digipeated twice.
        if (IsDuplicate(packet))
        {
            _logger.LogDebug("Suppressing duplicate packet from {Source} for digipeating.", packet.Source);
            return DigipeatReason.DuplicateSuppressed;
        }

        return null;
    }

    private static DigipeatReason? EvaluatePort(
        AprsPacket packet,
        PortInfo port,
        Guid sourcePortId,
        bool isAprsIsToRf,
        AprsSettings settings,
        IReadOnlyList<Callsign> allowlist,
        DigipeatPathDecision pathDecision,
        out AprsPacket? targetPacket)
    {
        targetPacket = null;

        if (port.Id == sourcePortId)
        {
            return DigipeatReason.SourcePortIsTarget;
        }

        if (!port.IsTxEnabled)
        {
            return DigipeatReason.TargetNotTransmitCapable;
        }

        if (!port.AllowDigipeat)
        {
            return DigipeatReason.TargetDoesNotAllowDigipeat;
        }

        if (isAprsIsToRf)
        {
            if (port.IsAprsIs)
            {
                return DigipeatReason.InternetToInternetNotForwarded;
            }

            if (!settings.EnableAprsIsToRfGate)
            {
                return DigipeatReason.InternetToRfDisabled;
            }

            if (!DigipeatPolicy.IsInternetToRfSourceAllowed(packet.Source, allowlist))
            {
                return DigipeatReason.InternetToRfSourceNotAllowed;
            }

            targetPacket = packet;
            return null;
        }

        if (port.IsAprsIs)
        {
            if (!settings.EnableRfToAprsIsGate)
            {
                return DigipeatReason.RfToInternetDisabled;
            }

            // RF-to-APRS-IS forwards the packet exactly as heard, preserving the path.
            targetPacket = packet;
            return null;
        }

        // RF-to-RF retransmits the mutated path so provenance and repeated bits travel
        // with the frame.
        if (pathDecision.Reason != DigipeatReason.Accepted)
        {
            return pathDecision.Reason;
        }

        targetPacket = packet with { Path = pathDecision.Path };
        return null;
    }

    private void Record(IReadOnlyList<DigipeatDecision> decisions, Guid sourcePortId, DateTimeOffset now)
    {
        foreach (DigipeatDecision decision in decisions)
        {
            _decisionLog.Record(now, sourcePortId, decision.TargetPortId, decision.Reason);
        }
    }

    private (IReadOnlyList<DigipeatTarget> Targets, IReadOnlyList<DigipeatDecision> Decisions) Reject(
        DigipeatReason packetLevelReject,
        Guid sourcePortId,
        DateTimeOffset now)
    {
        DigipeatDecision decision = new(TargetPortId: null, packetLevelReject);
        _decisionLog.Record(now, sourcePortId, targetPortId: null, packetLevelReject);

        _logger.LogDebug(
            "Digipeat candidate from source port {SourcePortId} rejected: {Reason}",
            sourcePortId,
            packetLevelReject);

        return ([], [decision]);
    }

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

        if (!Callsign.TryParse(settings.Callsign, out Callsign callsign))
        {
            return null;
        }

        int ssid = (int)settings.Ssid;
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
    /// Records the packet in the cross-port heard table and returns whether it was
    /// already seen within the retention window.
    /// </summary>
    private bool IsDuplicate(AprsPacket packet)
    {
        // Key on source + destination + info only, deliberately excluding the path:
        // RF→RF forwarding rewrites the path (inserting our callsign and decrementing
        // a hop), so a packet heard back after we digipeated it must still be
        // recognized as already seen rather than forwarded again. Hashing keeps the
        // bounded table independent of info-field length.
        int key = HashCode.Combine(packet.Source, packet.Destination, packet.Raw);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (_dedupLock)
        {
            if (_dedupTable.TryGetValue(key, out DateTimeOffset seen) && now - seen < DedupRetention)
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
                (int evictedKey, DateTimeOffset _) = _dedupOrder.Dequeue();
                _dedupTable.Remove(evictedKey);
            }

            return false;
        }
    }
}

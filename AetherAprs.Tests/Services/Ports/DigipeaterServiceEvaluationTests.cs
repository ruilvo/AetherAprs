// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration.Settings;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Aprs;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Ports;
using AetherAprs.Tests.Helpers;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace AetherAprs.Tests.Services.Ports;

/// <summary>
/// Golden-path tests for the forwarding service: each test asserts the exact accept or
/// reject reason a candidate produces, so a policy change shows up as a named failure.
/// </summary>
public sealed class DigipeaterServiceEvaluationTests
{
    private static readonly Guid SourcePortId = Guid.NewGuid();
    private static readonly Guid RfPortId = Guid.NewGuid();
    private static readonly Guid AprsIsPortId = Guid.NewGuid();

    [Fact]
    public void Evaluate_DigipeaterDisabled_RejectsWithDigipeaterDisabled()
    {
        DigipeaterService service = CreateService(enableDigipeater: false);

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        DigipeatDecision decision = Assert.Single(decisions);
        Assert.Null(decision.TargetPortId);
        Assert.Equal(DigipeatReason.DigipeaterDisabled, decision.Reason);
        Assert.Equal(1, service.GetDecisionCount(DigipeatReason.DigipeaterDisabled));
    }

    [Fact]
    public void Evaluate_OwnStationSource_RejectsWithOwnStationSource()
    {
        DigipeaterService service = CreateService(callsignBase: "N0CALL");

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(source: "N0CALL"),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Equal(DigipeatReason.OwnStationSource, Assert.Single(decisions).Reason);
    }

    [Fact]
    public void Evaluate_TelemetryPacket_RejectsWithPacketTypeNotForwardable()
    {
        DigipeaterService service = CreateService();
        TelemetryPacket packet = new()
        {
            Source = new Callsign("N5TEL"),
            Destination = new Callsign("APRS"),
            Path = [],
            Raw = "T#005,006,007,008,009,010,011",
            SequenceNumber = 5
        };

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            packet,
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Equal(DigipeatReason.PacketTypeNotForwardable, Assert.Single(decisions).Reason);
    }

    [Fact]
    public void Evaluate_PathAlreadyRepeatedByThisStation_RejectsWithLoopDetected()
    {
        DigipeaterService service = CreateService(callsignBase: "MYCALL", ssid: 9);

        PositionPacket packet = CreatePositionPacket() with
        {
            Path = [new Callsign("MYCALL", 9, hasBeenRepeated: true), new Callsign("WIDE2", 2)]
        };

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            packet,
            SourcePortId,
            sourcePortIsAprsIs: false,
            CreatePorts());

        Assert.Equal(DigipeatReason.LoopDetected, Assert.Single(decisions).Reason);
    }

    [Fact]
    public void Evaluate_DuplicateInsideTheWindow_RejectsWithDuplicateSuppressed()
    {
        TestClock clock = new();
        DigipeaterService service = CreateService(timeProvider: clock);
        PositionPacket packet = CreatePositionPacket();

        _ = service.Evaluate(packet, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());
        clock.Advance(TimeSpan.FromSeconds(29));
        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            packet,
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Equal(DigipeatReason.DuplicateSuppressed, Assert.Single(decisions).Reason);
    }

    [Fact]
    public void Evaluate_SamePacketAfterTheWindow_IsAcceptedAgain()
    {
        TestClock clock = new();
        DigipeaterService service = CreateService(timeProvider: clock);
        PositionPacket packet = CreatePositionPacket();

        _ = service.Evaluate(packet, SourcePortId, sourcePortIsAprsIs: true, CreatePorts());
        clock.Advance(TimeSpan.FromSeconds(31));
        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            packet,
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Contains(decisions, decision => decision.Reason == DigipeatReason.Accepted);
    }

    [Fact]
    public void Evaluate_AliasNotEnabled_RejectsRfTargetButStillGatesToAprsIs()
    {
        DigipeaterService service = CreateService(callsignBase: "MYCALL", respondToWide1: false);

        PositionPacket packet = CreatePositionPacket() with
        {
            Path = [new Callsign("WIDE1", 1)]
        };

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            packet,
            SourcePortId,
            sourcePortIsAprsIs: false,
            CreatePorts());

        Assert.Equal(
            DigipeatReason.AliasNotEnabled,
            Assert.Single(decisions, decision => decision.TargetPortId == RfPortId).Reason);
        Assert.Equal(
            DigipeatReason.Accepted,
            Assert.Single(decisions, decision => decision.TargetPortId == AprsIsPortId).Reason);
    }

    [Fact]
    public void Evaluate_ExhaustedAlias_RejectsRfTargetWithHopsExhausted()
    {
        DigipeaterService service = CreateService(callsignBase: "MYCALL", respondToWide2: true);

        PositionPacket packet = CreatePositionPacket() with
        {
            Path = [new Callsign("WIDE2", 0)]
        };

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            packet,
            SourcePortId,
            sourcePortIsAprsIs: false,
            CreatePorts());

        Assert.Equal(
            DigipeatReason.HopsExhausted,
            Assert.Single(decisions, decision => decision.TargetPortId == RfPortId).Reason);
    }

    [Fact]
    public void Evaluate_SourcePort_IsReportedAsSourcePortIsTarget()
    {
        DigipeaterService service = CreateService();
        List<PortInfo> ports =
        [
            new(SourcePortId, IsAprsIs: false, IsTxEnabled: true, AllowDigipeat: true)
        ];

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: false,
            ports);

        DigipeatDecision decision = Assert.Single(decisions);
        Assert.Equal(SourcePortId, decision.TargetPortId);
        Assert.Equal(DigipeatReason.SourcePortIsTarget, decision.Reason);
    }

    [Fact]
    public void Evaluate_PortWithoutTx_IsReportedAsNotTransmitCapable()
    {
        DigipeaterService service = CreateService();
        List<PortInfo> ports =
        [
            new(RfPortId, IsAprsIs: false, IsTxEnabled: false, AllowDigipeat: true)
        ];

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            ports);

        Assert.Equal(DigipeatReason.TargetNotTransmitCapable, Assert.Single(decisions).Reason);
    }

    [Fact]
    public void Evaluate_PortWithoutAllowDigipeat_IsReportedAsNotAllowed()
    {
        DigipeaterService service = CreateService();
        List<PortInfo> ports =
        [
            new(RfPortId, IsAprsIs: false, IsTxEnabled: true, AllowDigipeat: false)
        ];

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            ports);

        Assert.Equal(DigipeatReason.TargetDoesNotAllowDigipeat, Assert.Single(decisions).Reason);
    }

    [Fact]
    public void Evaluate_AprsIsToRfGateDisabled_RejectsRfTarget()
    {
        DigipeaterService service = CreateService(enableAprsIsToRfGate: false);

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.All(
            decisions.Where(decision => decision.TargetPortId == RfPortId),
            decision => Assert.Equal(DigipeatReason.InternetToRfDisabled, decision.Reason));
    }

    [Fact]
    public void Evaluate_AprsIsSourceToAprsIsTarget_IsNeverForwarded()
    {
        DigipeaterService service = CreateService(enableAprsIsToRfGate: true);

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Equal(
            DigipeatReason.InternetToInternetNotForwarded,
            Assert.Single(decisions, decision => decision.TargetPortId == AprsIsPortId).Reason);
    }

    [Fact]
    public void Evaluate_RfToAprsIsGateDisabled_RejectsTheAprsIsTarget()
    {
        DigipeaterService service = CreateService(enableRfToAprsIsGate: false);

        PositionPacket packet = CreatePositionPacket() with
        {
            Path = [new Callsign("WIDE2", 2)]
        };

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            packet,
            SourcePortId,
            sourcePortIsAprsIs: false,
            CreatePorts());

        Assert.Equal(
            DigipeatReason.RfToInternetDisabled,
            Assert.Single(decisions, decision => decision.TargetPortId == AprsIsPortId).Reason);
        Assert.Equal(
            DigipeatReason.Accepted,
            Assert.Single(decisions, decision => decision.TargetPortId == RfPortId).Reason);
    }

    [Fact]
    public void Evaluate_AprsIsSourceOnTheAllowlist_IsGatedToRf()
    {
        DigipeaterService service = CreateService(
            enableAprsIsToRfGate: true,
            aprsIsToRfAllowlist: "N5SRC-4");

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(source: "N5SRC", ssid: 4),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Equal(
            DigipeatReason.Accepted,
            Assert.Single(decisions, decision => decision.TargetPortId == RfPortId).Reason);
    }

    [Fact]
    public void Evaluate_AprsIsSourceOffTheAllowlist_IsNotGatedToRf()
    {
        DigipeaterService service = CreateService(
            enableAprsIsToRfGate: true,
            aprsIsToRfAllowlist: "N9OTHR");

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(source: "N5SRC", ssid: 4),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Equal(
            DigipeatReason.InternetToRfSourceNotAllowed,
            Assert.Single(decisions, decision => decision.TargetPortId == RfPortId).Reason);
    }

    [Fact]
    public void Evaluate_AllowlistBaseEntry_MatchesEverySsid()
    {
        DigipeaterService service = CreateService(
            enableAprsIsToRfGate: true,
            aprsIsToRfAllowlist: "N5SRC");

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(source: "N5SRC", ssid: 7),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Equal(
            DigipeatReason.Accepted,
            Assert.Single(decisions, decision => decision.TargetPortId == RfPortId).Reason);
    }

    [Fact]
    public void GetDigipeatTargets_ThirdPartyRfFrame_KeepsEncapsulationAndMutatesThePath()
    {
        DigipeaterService service = CreateService(callsignBase: "MYCALL", ssid: 9);

        ThirdPartyPacket packet = new()
        {
            Source = new Callsign("N5SRC", 7),
            Destination = new Callsign("APRS"),
            Path = [new Callsign("WIDE2", 1)],
            Raw = "}N0INNER>APRS,TCPIP*:!4903.50N/07202.75W-Third party",
            Encapsulated = "N0INNER>APRS,TCPIP*:!4903.50N/07202.75W-Third party"
        };

        DigipeatTarget target = Assert.Single(
            service.GetDigipeatTargets(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts()),
            candidate => candidate.PortId == RfPortId);

        ThirdPartyPacket emitted = Assert.IsType<ThirdPartyPacket>(target.Packet);

        // The encapsulation is the payload of a third-party frame: forwarding must
        // preserve it byte for byte and carry it inside the mutated AX.25 path.
        Assert.Equal(packet.Encapsulated, emitted.Encapsulated);
        Assert.Equal(
            "}N0INNER>APRS,TCPIP*:!4903.50N/07202.75W-Third party",
            AprsInfoFieldSerializer.FormatInfoField(emitted));
        Assert.Equal(
            [
                new Callsign("MYCALL", 9, hasBeenRepeated: true),
                new Callsign("WIDE2", 0, hasBeenRepeated: true),
            ],
            emitted.Path);
    }

    [Fact]
    public void Evaluate_DefaultSettings_ForwardNothingUntilTheDigipeaterAndDirectionAreEnabled()
    {
        // Both gates keep their persisted false default, so turning the digipeater
        // on is not enough to move a packet across the network boundary.
        DigipeaterService service = CreateService(
            enableDigipeater: true,
            enableAprsIsToRfGate: false,
            enableRfToAprsIsGate: false);

        IReadOnlyList<DigipeatDecision> aprsIsToRf = service.Evaluate(
            CreatePositionPacket(source: "N5SRC", ssid: 7, raw: "!4903.50N/07202.75W-From the Internet"),
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Equal(
            DigipeatReason.InternetToRfDisabled,
            Assert.Single(aprsIsToRf, decision => decision.TargetPortId == RfPortId).Reason);

        IReadOnlyList<DigipeatDecision> rfToAprsIs = service.Evaluate(
            CreatePositionPacket(source: "N5SRC", ssid: 7, raw: "!4903.50N/07202.75W-From RF"),
            SourcePortId,
            sourcePortIsAprsIs: false,
            CreatePorts());

        Assert.Equal(
            DigipeatReason.RfToInternetDisabled,
            Assert.Single(rfToAprsIs, decision => decision.TargetPortId == AprsIsPortId).Reason);
    }

    [Fact]
    public void Evaluate_DigipeaterDisabled_RejectsBothGatingDirectionsBeforeThePortGate()
    {
        DigipeaterService service = CreateService(
            enableDigipeater: false,
            enableAprsIsToRfGate: true,
            enableRfToAprsIsGate: true);

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(source: "N5SRC", ssid: 7),
            SourcePortId,
            sourcePortIsAprsIs: false,
            CreatePorts());

        Assert.Equal(DigipeatReason.DigipeaterDisabled, Assert.Single(decisions).Reason);
    }

    [Fact]
    public void Evaluate_ThirdPartyPacketFromAnAllowlistedSource_IsGatedToRf()
    {
        DigipeaterService service = CreateService(
            enableAprsIsToRfGate: true,
            aprsIsToRfAllowlist: "N5SRC");

        ThirdPartyPacket packet = new()
        {
            Source = new Callsign("N5SRC"),
            Destination = new Callsign("APRS"),
            Path = [],
            Raw = "}N0INNER>APRS,TCPIP*:!4903.50N/07202.75W-Third party",
            Encapsulated = "N0INNER>APRS,TCPIP*:!4903.50N/07202.75W-Third party"
        };

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            packet,
            SourcePortId,
            sourcePortIsAprsIs: true,
            CreatePorts());

        Assert.Equal(
            DigipeatReason.Accepted,
            Assert.Single(decisions, decision => decision.TargetPortId == RfPortId).Reason);
    }

    [Fact]
    public void Evaluate_NoAvailablePorts_IsReportedAsNoTargetPorts()
    {
        DigipeaterService service = CreateService();

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(),
            SourcePortId,
            sourcePortIsAprsIs: true,
            []);

        DigipeatDecision decision = Assert.Single(decisions);
        Assert.Null(decision.TargetPortId);
        Assert.Equal(DigipeatReason.NoTargetPorts, decision.Reason);
    }

    [Fact]
    public void Evaluate_RateLimitExceeded_RejectsTheCandidateForEveryPort()
    {
        TestClock clock = new();
        DigipeaterService service = CreateService(
            timeProvider: clock,
            rateLimit: new DigipeatRateLimitOptions { MaxForwardsPerWindow = 2, Window = TimeSpan.FromMinutes(1) });

        _ = service.Evaluate(CreatePositionPacket(raw: "!4903.50N/07202.75W-One"), SourcePortId, true, CreatePorts());
        _ = service.Evaluate(CreatePositionPacket(raw: "!4903.50N/07202.75W-Two"), SourcePortId, true, CreatePorts());

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(raw: "!4903.50N/07202.75W-Three"),
            SourcePortId,
            true,
            CreatePorts());

        DigipeatDecision decision = Assert.Single(decisions);
        Assert.Null(decision.TargetPortId);
        Assert.Equal(DigipeatReason.RateLimited, decision.Reason);
        Assert.Empty(service.GetDigipeatTargets(
            CreatePositionPacket(raw: "!4903.50N/07202.75W-Four"),
            SourcePortId,
            true,
            CreatePorts()));
        Assert.Equal(2, service.GetDecisionCount(DigipeatReason.RateLimited));
    }

    [Fact]
    public void Evaluate_RateLimitAfterTheWindow_AllowsTheCandidateAgain()
    {
        TestClock clock = new();
        DigipeaterService service = CreateService(
            timeProvider: clock,
            rateLimit: new DigipeatRateLimitOptions { MaxForwardsPerWindow = 1, Window = TimeSpan.FromMinutes(1) });

        _ = service.Evaluate(CreatePositionPacket(raw: "!4903.50N/07202.75W-One"), SourcePortId, true, CreatePorts());
        clock.Advance(TimeSpan.FromMinutes(1));

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(raw: "!4903.50N/07202.75W-Two"),
            SourcePortId,
            true,
            CreatePorts());

        Assert.Contains(decisions, decision => decision.Reason == DigipeatReason.Accepted);
    }

    [Fact]
    public void Evaluate_RejectedCandidates_DoNotConsumeTheRateLimit()
    {
        DigipeaterService service = CreateService(
            rateLimit: new DigipeatRateLimitOptions { MaxForwardsPerWindow = 1, Window = TimeSpan.FromMinutes(1) });

        // Own-station packets are rejected before any route exists, so they must not
        // use up the one slot the source port has.
        _ = service.Evaluate(CreatePositionPacket(source: "N0CALL"), SourcePortId, true, CreatePorts());
        _ = service.Evaluate(CreatePositionPacket(source: "N0CALL"), SourcePortId, true, CreatePorts());

        IReadOnlyList<DigipeatDecision> decisions = service.Evaluate(
            CreatePositionPacket(),
            SourcePortId,
            true,
            CreatePorts());

        Assert.Contains(decisions, decision => decision.Reason == DigipeatReason.Accepted);
    }

    [Fact]
    public void GetRecentDecisions_RecordsTargetPortsAndReasons()
    {
        DigipeaterService service = CreateService(callsignBase: "MYCALL");

        PositionPacket packet = CreatePositionPacket() with
        {
            Path = [new Callsign("WIDE2", 2)]
        };

        _ = service.Evaluate(packet, SourcePortId, sourcePortIsAprsIs: false, CreatePorts());

        IReadOnlyList<DigipeatDecisionRecord> recent = service.GetRecentDecisions();

        Assert.Equal(2, recent.Count);
        Assert.All(recent, record =>
        {
            Assert.Equal(SourcePortId, record.SourcePortId);
            Assert.Equal(DigipeatReason.Accepted, record.Reason);
        });
        Assert.Contains(recent, record => record.TargetPortId == RfPortId);
        Assert.Contains(recent, record => record.TargetPortId == AprsIsPortId);
        Assert.Equal(2, service.GetDecisionCount(DigipeatReason.Accepted));
    }

    [Fact]
    public void GetRecentDecisions_HistoryStaysBounded()
    {
        TestClock clock = new();
        DigipeaterService service = CreateService(
            timeProvider: clock,
            decisionLog: new DigipeatDecisionLog(capacity: 2));

        _ = service.Evaluate(CreatePositionPacket(raw: "!4903.50N/07202.75W-One"), SourcePortId, true, CreatePorts());
        _ = service.Evaluate(CreatePositionPacket(raw: "!4903.50N/07202.75W-Two"), SourcePortId, true, CreatePorts());

        // Two evaluations, each with one accepted RF target and one gated APRS-IS
        // target: the history keeps only the last two records, the counters keep all.
        Assert.Equal(2, service.GetRecentDecisions().Count);
        Assert.Equal(2, service.GetDecisionCount(DigipeatReason.Accepted));
    }

    private static PositionPacket CreatePositionPacket(
        string source = "N5SRC",
        int? ssid = null,
        string raw = "!4903.50N/07202.75W-Test comment") => new()
        {
            Source = new Callsign(source, ssid),
            Destination = new Callsign("APRS"),
            Path = [],
            Raw = raw
        };

    private static IReadOnlyList<PortInfo> CreatePorts() =>
    [
        new PortInfo(RfPortId, IsAprsIs: false, IsTxEnabled: true, AllowDigipeat: true),
        new PortInfo(AprsIsPortId, IsAprsIs: true, IsTxEnabled: true, AllowDigipeat: true)
    ];

    private static DigipeaterService CreateService(
        bool enableDigipeater = true,
        bool enableAprsIsToRfGate = true,
        bool enableRfToAprsIsGate = true,
        string callsignBase = "N0CALL",
        byte ssid = 0,
        bool respondToWide1 = false,
        bool respondToWide2 = true,
        bool respondToTrace = false,
        bool digipeaterInsertCallsign = true,
        string? aprsIsToRfAllowlist = null,
        DigipeatRateLimitOptions? rateLimit = null,
        DigipeatDecisionLog? decisionLog = null,
        TimeProvider? timeProvider = null)
    {
        AppSettings settings = new()
        {
            Aprs = new AprsSettings
            {
                EnableDigipeater = enableDigipeater,
                EnableAprsIsToRfGate = enableAprsIsToRfGate,
                EnableRfToAprsIsGate = enableRfToAprsIsGate,
                AprsIsToRfAllowlist = aprsIsToRfAllowlist,
                Callsign = callsignBase,
                Ssid = (AprsSsid)ssid,
                DigipeaterRespondToWide1 = respondToWide1,
                DigipeaterRespondToWide2 = respondToWide2,
                DigipeaterRespondToTrace = respondToTrace,
                DigipeaterInsertCallsign = digipeaterInsertCallsign,
            },
        };

        return new DigipeaterService(
            new StubConfigurationService(settings),
            NullLogger<DigipeaterService>.Instance,
            timeProvider,
            rateLimit,
            decisionLog);
    }

    private sealed class StubConfigurationService(AppSettings settings) : IConfigurationService
    {
#pragma warning disable CS0067 // Event is never used in this test double
        public event EventHandler? SettingsChanged;
#pragma warning restore CS0067

        public AppSettings Current { get; private set; } = settings;

        public Task<AppSettings> UpdateAsync(
            Func<AppSettings, AppSettings> update,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current = update(Current));
    }
}

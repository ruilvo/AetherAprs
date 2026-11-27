// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Ports;
using Xunit;

namespace AetherAprs.Tests.Services.Ports;

/// <summary>
/// Golden-path tests for the pure forwarding policy. Every assertion names the exact
/// reason and the exact retransmission path so a policy change is visible in the diff.
/// </summary>
public sealed class DigipeatPolicyTests
{
    private static readonly Callsign OwnCallsign = new("MYCALL", 9);

    [Fact]
    public void IsDigipeatable_SerializablePacket_IsForwardable()
    {
        PositionPacket packet = new()
        {
            Source = new Callsign("N5SRC"),
            Destination = new Callsign("APRS"),
            Path = [],
            Raw = "!4903.50N/07202.75W-Test comment"
        };

        Assert.True(DigipeatPolicy.IsDigipeatable(packet));
    }

    [Fact]
    public void IsDigipeatable_TelemetryPacket_IsNotForwardable()
    {
        TelemetryPacket packet = new()
        {
            Source = new Callsign("N5TEL"),
            Destination = new Callsign("APRS"),
            Path = [],
            Raw = "T#005,006,007,008,009,010,011",
            SequenceNumber = 5
        };

        Assert.False(DigipeatPolicy.IsDigipeatable(packet));
    }

    [Fact]
    public void IsDigipeatable_CapabilitiesPacket_IsNotForwardable()
    {
        CapabilitiesPacket packet = new()
        {
            Source = new Callsign("N5CAP"),
            Destination = new Callsign("APRS"),
            Path = [],
            Raw = "<STATIONCAPABILITIES>",
            Text = "STATIONCAPABILITIES"
        };

        Assert.False(DigipeatPolicy.IsDigipeatable(packet));
    }

    [Theory]
    [InlineData("WIDE1", true)]
    [InlineData("WIDE2", true)]
    [InlineData("TRACE", true)]
    [InlineData("RELAY", false)]
    [InlineData("MYCALL", false)]
    public void IsDigipeaterAlias_RecognizesOnlyWideAndTrace(string callsignBase, bool expected)
    {
        Assert.Equal(expected, DigipeatPolicy.IsDigipeaterAlias(new Callsign(callsignBase)));
    }

    [Fact]
    public void RespondsToAlias_DefaultPolicy_ServesOnlyWide2()
    {
        DigipeatAliasPolicy policy = DigipeatAliasPolicy.Default;

        Assert.False(DigipeatPolicy.RespondsToAlias(new Callsign("WIDE1", 1), policy));
        Assert.True(DigipeatPolicy.RespondsToAlias(new Callsign("WIDE2", 1), policy));
        Assert.False(DigipeatPolicy.RespondsToAlias(new Callsign("TRACE", 1), policy));
        Assert.False(DigipeatPolicy.RespondsToAlias(new Callsign("MYCALL", 1), policy));
    }

    [Fact]
    public void EvaluatePath_Wide1WithFillInEnabled_InsertsCallsignAndDecrements()
    {
        DigipeatAliasPolicy policy = new() { RespondToWide1 = true, RespondToWide2 = false };

        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("WIDE1", 1)],
            OwnCallsign,
            policy);

        Assert.Equal(DigipeatReason.Accepted, decision.Reason);
        Assert.Equal(["MYCALL-9*", "WIDE1-0*"], Describe(decision.Path));
    }

    [Fact]
    public void EvaluatePath_Wide2WithDefaultPolicy_InsertsCallsignAndDecrements()
    {
        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("WIDE2", 2)],
            OwnCallsign,
            DigipeatAliasPolicy.Default);

        Assert.Equal(DigipeatReason.Accepted, decision.Reason);
        Assert.Equal(["MYCALL-9*", "WIDE2-1*"], Describe(decision.Path));
    }

    [Fact]
    public void EvaluatePath_AliasNotEnabled_IsRejected()
    {
        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("WIDE1", 1)],
            OwnCallsign,
            DigipeatAliasPolicy.Default);

        Assert.Equal(DigipeatReason.AliasNotEnabled, decision.Reason);
    }

    [Fact]
    public void EvaluatePath_TraceAlwaysInsertsCallsignEvenWhenInsertIsDisabled()
    {
        DigipeatAliasPolicy policy = new()
        {
            RespondToTrace = true,
            InsertCallsign = false
        };

        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("TRACE", 3)],
            OwnCallsign,
            policy);

        Assert.Equal(DigipeatReason.Accepted, decision.Reason);
        Assert.Equal(["MYCALL-9*", "TRACE-2*"], Describe(decision.Path));
    }

    [Fact]
    public void EvaluatePath_InsertCallsignDisabled_DecrementsWithoutInserting()
    {
        DigipeatAliasPolicy policy = new()
        {
            RespondToWide2 = true,
            InsertCallsign = false
        };

        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("WIDE2", 2)],
            OwnCallsign,
            policy);

        Assert.Equal(DigipeatReason.Accepted, decision.Reason);
        Assert.Equal(["WIDE2-1*"], Describe(decision.Path));
    }

    [Fact]
    public void EvaluatePath_ExhaustedAlias_IsRejected()
    {
        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("WIDE2", 0)],
            OwnCallsign,
            DigipeatAliasPolicy.Default);

        Assert.Equal(DigipeatReason.HopsExhausted, decision.Reason);
    }

    [Fact]
    public void EvaluatePath_ExhaustedAliasBeforeServiceableHop_IsRejected()
    {
        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("WIDE1", 0), new Callsign("WIDE2", 2)],
            OwnCallsign,
            new DigipeatAliasPolicy { RespondToWide1 = true, RespondToWide2 = true });

        Assert.Equal(DigipeatReason.HopsExhausted, decision.Reason);
    }

    [Fact]
    public void EvaluatePath_OnlyForeignExplicitCallsigns_IsRejected()
    {
        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("K1ABC", 1)],
            OwnCallsign,
            DigipeatAliasPolicy.Default);

        Assert.Equal(DigipeatReason.NoServiceableAlias, decision.Reason);
        Assert.Equal(["K1ABC-1"], Describe(decision.Path));
    }

    [Fact]
    public void EvaluatePath_OnlyRepeatedHops_IsRejected()
    {
        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("WIDE2", 1, hasBeenRepeated: true)],
            OwnCallsign,
            DigipeatAliasPolicy.Default);

        Assert.Equal(DigipeatReason.NoServiceableAlias, decision.Reason);
    }

    [Fact]
    public void EvaluatePath_OwnRepeatedHop_IsALoop()
    {
        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("MYCALL", 9, hasBeenRepeated: true), new Callsign("WIDE2", 2)],
            OwnCallsign,
            DigipeatAliasPolicy.Default);

        Assert.Equal(DigipeatReason.LoopDetected, decision.Reason);
    }

    [Fact]
    public void EvaluatePath_OwnHopWithMatchingSsidZero_IsALoop()
    {
        // SSID 0 and no SSID name the same station, so a repeated "MYCALL-0" hop is
        // still this station's own repeated hop.
        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("MYCALL", 0, hasBeenRepeated: true)],
            new Callsign("MYCALL"),
            DigipeatAliasPolicy.Default);

        Assert.Equal(DigipeatReason.LoopDetected, decision.Reason);
    }

    [Fact]
    public void EvaluatePath_UnrepeatedOwnHop_IsServicedInPlace()
    {
        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("MYCALL", 9), new Callsign("WIDE2", 2)],
            OwnCallsign,
            DigipeatAliasPolicy.Default);

        Assert.Equal(DigipeatReason.Accepted, decision.Reason);
        Assert.Equal(["MYCALL-9*", "WIDE2-2"], Describe(decision.Path));
    }

    [Fact]
    public void EvaluatePath_AlreadyRepeatedHop_IsSkippedForTheNextAlias()
    {
        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("WIDE1", 1, hasBeenRepeated: true), new Callsign("WIDE2", 2)],
            OwnCallsign,
            DigipeatAliasPolicy.Default);

        Assert.Equal(DigipeatReason.Accepted, decision.Reason);
        Assert.Equal(["WIDE1-1*", "MYCALL-9*", "WIDE2-1*"], Describe(decision.Path));
    }

    [Fact]
    public void EvaluatePath_HopsAfterTheServicedAlias_ArePreserved()
    {
        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("WIDE2", 2), new Callsign("WIDE1", 1)],
            OwnCallsign,
            DigipeatAliasPolicy.Default);

        Assert.Equal(DigipeatReason.Accepted, decision.Reason);
        Assert.Equal(["MYCALL-9*", "WIDE2-1*", "WIDE1-1"], Describe(decision.Path));
    }

    [Fact]
    public void EvaluatePath_EmptyPath_PrependsOwnCallsign()
    {
        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath([], OwnCallsign, DigipeatAliasPolicy.Default);

        Assert.Equal(DigipeatReason.Accepted, decision.Reason);
        Assert.Equal(["MYCALL-9*"], Describe(decision.Path));
    }

    [Fact]
    public void EvaluatePath_NoOwnCallsign_ForwardsPathUnchanged()
    {
        DigipeatPathDecision decision = DigipeatPolicy.EvaluatePath(
            [new Callsign("WIDE2", 2)],
            ownCallsign: null,
            DigipeatAliasPolicy.Default);

        Assert.Equal(DigipeatReason.Accepted, decision.Reason);
        Assert.Equal(["WIDE2-2"], Describe(decision.Path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseAllowlist_MissingText_IsEmpty(string? allowlist)
    {
        Assert.Empty(DigipeatPolicy.ParseAllowlist(allowlist));
    }

    [Fact]
    public void ParseAllowlist_MixedSeparatorsAndMalformedEntries_KeepsValidCallsigns()
    {
        IReadOnlyList<Callsign> entries = DigipeatPolicy.ParseAllowlist(
            "N0CALL-1, K1ABC;W1AW\tTOOLONGCALL,K2DEF-16, K3GHI-15");

        Assert.Equal(
            ["N0CALL-1", "K1ABC", "W1AW", "K3GHI-15"],
            entries.Select(entry => entry.ToString()));
    }

    [Fact]
    public void SplitAllowlist_KeepsEveryEntrySoTheFieldCanRejectTheMalformedOnes()
    {
        // The settings field validates the split entries before they are parsed:
        // dropping a malformed entry here would silently widen the saved allowlist.
        IReadOnlyList<string> entries = DigipeatPolicy.SplitAllowlist(
            "N0CALL-1, K1ABC;W1AW\tTOOLONGCALL,K2DEF-16");

        Assert.Equal(["N0CALL-1", "K1ABC", "W1AW", "TOOLONGCALL", "K2DEF-16"], entries);
        Assert.Equal(3, DigipeatPolicy.ParseAllowlist("N0CALL-1, K1ABC;W1AW\tTOOLONGCALL").Count);
    }

    [Fact]
    public void IsInternetToRfSourceAllowed_EmptyAllowlist_AllowsEverySource()
    {
        Assert.True(DigipeatPolicy.IsInternetToRfSourceAllowed(new Callsign("N9SRC", 4), []));
    }

    [Fact]
    public void IsInternetToRfSourceAllowed_BaseEntry_MatchesEverySsidOfThatBase()
    {
        IReadOnlyList<Callsign> allowlist = DigipeatPolicy.ParseAllowlist("N9SRC");

        Assert.True(DigipeatPolicy.IsInternetToRfSourceAllowed(new Callsign("N9SRC", 4), allowlist));
        Assert.True(DigipeatPolicy.IsInternetToRfSourceAllowed(new Callsign("N9SRC"), allowlist));
    }

    [Fact]
    public void IsInternetToRfSourceAllowed_SsidEntry_RequiresTheExactSsid()
    {
        IReadOnlyList<Callsign> allowlist = DigipeatPolicy.ParseAllowlist("N9SRC-4");

        Assert.True(DigipeatPolicy.IsInternetToRfSourceAllowed(new Callsign("N9SRC", 4), allowlist));
        Assert.False(DigipeatPolicy.IsInternetToRfSourceAllowed(new Callsign("N9SRC", 5), allowlist));
        Assert.False(DigipeatPolicy.IsInternetToRfSourceAllowed(new Callsign("N9SRC"), allowlist));
    }

    [Fact]
    public void IsInternetToRfSourceAllowed_BaseMismatch_IsDenied()
    {
        IReadOnlyList<Callsign> allowlist = DigipeatPolicy.ParseAllowlist("N9SRC-4");

        Assert.False(DigipeatPolicy.IsInternetToRfSourceAllowed(new Callsign("N9XXX", 4), allowlist));
    }

    /// <summary>
    /// Renders a path as base, SSID and repeated marker so the golden expectations stay
    /// readable and distinguish <c>-0</c> from a missing SSID.
    /// </summary>
    private static IReadOnlyList<string> Describe(IReadOnlyList<Callsign> path) =>
    [
        .. path.Select(hop =>
            $"{hop.Base}{(hop.Ssid is int ssid ? $"-{ssid}" : string.Empty)}{(hop.HasBeenRepeated ? "*" : string.Empty)}")
    ];
}

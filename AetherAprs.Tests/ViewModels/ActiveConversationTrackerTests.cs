// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.ViewModels;

using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class ActiveConversationTrackerTests
{
    private static readonly AprsAddress Peer = AprsAddress.FromCallsign(new Callsign("K0PEER", 1));
    private static readonly AprsAddress Other = AprsAddress.FromCallsign(new Callsign("K0OTH", 2));

    [Fact]
    public void Activate_TracksTheVisibleConversation()
    {
        ActiveConversationTracker tracker = new();

        tracker.Activate(Peer);

        Assert.Equal(Peer, tracker.Peer);
    }

    [Fact]
    public void ActivateNull_ClearsTheVisibleConversation()
    {
        ActiveConversationTracker tracker = new();
        tracker.Activate(Peer);

        tracker.Activate(null);

        Assert.Null(tracker.Peer);
    }

    [Fact]
    public void Clear_MatchingPeer_ClearsTheVisibleConversation()
    {
        ActiveConversationTracker tracker = new();
        tracker.Activate(Peer);

        tracker.Clear(Peer);

        Assert.Null(tracker.Peer);
    }

    [Fact]
    public void Clear_DifferentPeer_KeepsTheVisibleConversation()
    {
        ActiveConversationTracker tracker = new();
        tracker.Activate(Peer);

        tracker.Clear(Other);

        Assert.Equal(Peer, tracker.Peer);
    }
}

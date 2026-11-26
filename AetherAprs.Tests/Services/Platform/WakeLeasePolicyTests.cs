// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;
using System;
using Xunit;

namespace AetherAprs.Tests.Services.Platform;

public sealed class WakeLeasePolicyTests
{
    [Fact]
    public void Decide_WhenWorkArrivesWithoutALease_ArmsIt() => Assert.Equal(WakeLeaseDecision.Arm, WakeLeasePolicy.Decide(isSessionActive: true, isLeaseHeld: false));

    [Fact]
    public void Decide_WhenTheSessionEndsWithALease_ReleasesIt() => Assert.Equal(WakeLeaseDecision.Release, WakeLeasePolicy.Decide(isSessionActive: false, isLeaseHeld: true));

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Decide_WhenTheLeaseAlreadyMatchesTheSession_LeavesItAlone(bool isSessionActive, bool isLeaseHeld) =>
        // Re-acquiring a held lock would add a platform reference that the matching
        // single release cannot clear, so a held lease is never extended.
        Assert.Equal(WakeLeaseDecision.None, WakeLeasePolicy.Decide(isSessionActive, isLeaseHeld));

    [Fact]
    public void Lease_IsBoundedAndPositive()
    {
        Assert.True(WakeLeasePolicy.LeaseMilliseconds > 0);
        Assert.True(
            WakeLeasePolicy.Lease <= TimeSpan.FromMinutes(15),
            $"A keep-alive wake lease must stay bounded, but it is {WakeLeasePolicy.Lease}.");
        Assert.Equal(TimeSpan.FromMilliseconds(WakeLeasePolicy.LeaseMilliseconds), WakeLeasePolicy.Lease);
    }
}

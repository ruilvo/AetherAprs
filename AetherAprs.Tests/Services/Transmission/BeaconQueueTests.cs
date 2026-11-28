// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.Services.Transmission;

using Xunit;

namespace AetherAprs.Tests.Services.Transmission;

public sealed class BeaconQueueTests
{
    [Theory]
    [InlineData(BeaconQueueStatus.Queued)]
    [InlineData(BeaconQueueStatus.Sending)]
    public void SlotHoldingStates_AreOpen(BeaconQueueStatus status)
    {
        Assert.True(BeaconQueue.IsOpen(status));
        Assert.False(BeaconQueue.IsTerminal(status));
    }

    [Theory]
    [InlineData(BeaconQueueStatus.Sent)]
    [InlineData(BeaconQueueStatus.Failed)]
    [InlineData(BeaconQueueStatus.Expired)]
    [InlineData(BeaconQueueStatus.Dropped)]
    public void FinishedStates_AreTerminal(BeaconQueueStatus status)
    {
        Assert.False(BeaconQueue.IsOpen(status));
        Assert.True(BeaconQueue.IsTerminal(status));
    }

    [Fact]
    public void QueuedBeacon_BeginsSending()
    {
        Assert.Equal(
            BeaconQueueStatus.Sending,
            BeaconQueue.Fire(BeaconQueueStatus.Queued, BeaconQueueTrigger.BeginSending));
    }

    [Theory]
    [InlineData(BeaconQueueStatus.Queued)]
    [InlineData(BeaconQueueStatus.Sending)]
    public void AnyOpenBeacon_CanBeSent(BeaconQueueStatus status)
    {
        Assert.Equal(
            BeaconQueueStatus.Sent,
            BeaconQueue.Fire(status, BeaconQueueTrigger.Sent));
    }

    [Theory]
    [InlineData(BeaconQueueStatus.Queued)]
    [InlineData(BeaconQueueStatus.Sending)]
    public void AnyOpenBeacon_CanExpire(BeaconQueueStatus status)
    {
        Assert.Equal(
            BeaconQueueStatus.Expired,
            BeaconQueue.Fire(status, BeaconQueueTrigger.Expire));
    }

    [Fact]
    public void QueuedBeacon_CanBeDroppedForCapacity()
    {
        Assert.Equal(
            BeaconQueueStatus.Dropped,
            BeaconQueue.Fire(BeaconQueueStatus.Queued, BeaconQueueTrigger.Drop));
    }

    [Fact]
    public void SendingBeacon_CannotBeDropped()
    {
        // Capacity eviction only takes a beacon that is not already on the wire.
        Assert.False(BeaconQueue.CanFire(BeaconQueueStatus.Sending, BeaconQueueTrigger.Drop));
    }

    [Fact]
    public void SendingBeacon_ReturnsToTheQueueWhenReconciled()
    {
        Assert.Equal(
            BeaconQueueStatus.Queued,
            BeaconQueue.Fire(BeaconQueueStatus.Sending, BeaconQueueTrigger.Retry));
    }

    [Fact]
    public void FailedAttempt_WithBudgetLeft_KeepsTheBeaconQueued()
    {
        bool recorded = BeaconQueue.TryFailAttempt(
            BeaconQueueStatus.Queued,
            retryBudgetSpent: false,
            out BeaconQueueStatus next);

        Assert.True(recorded);
        Assert.Equal(BeaconQueueStatus.Queued, next);
    }

    [Fact]
    public void FailedAttempt_WhileSending_WithBudgetLeft_ReturnsToTheQueue()
    {
        bool recorded = BeaconQueue.TryFailAttempt(
            BeaconQueueStatus.Sending,
            retryBudgetSpent: false,
            out BeaconQueueStatus next);

        Assert.True(recorded);
        Assert.Equal(BeaconQueueStatus.Queued, next);
    }

    [Theory]
    [InlineData(BeaconQueueStatus.Queued)]
    [InlineData(BeaconQueueStatus.Sending)]
    public void FailedAttempt_WithNoBudgetLeft_IsTerminal(BeaconQueueStatus status)
    {
        bool recorded = BeaconQueue.TryFailAttempt(status, retryBudgetSpent: true, out BeaconQueueStatus next);

        Assert.True(recorded);
        Assert.Equal(BeaconQueueStatus.Failed, next);
    }

    [Theory]
    [InlineData(BeaconQueueStatus.Sent)]
    [InlineData(BeaconQueueStatus.Failed)]
    [InlineData(BeaconQueueStatus.Expired)]
    [InlineData(BeaconQueueStatus.Dropped)]
    public void TerminalBeacon_RejectsEveryTrigger(BeaconQueueStatus status)
    {
        // A late send callback must not resurrect a beacon that was dropped for
        // capacity or already expired.
        foreach (BeaconQueueTrigger trigger in Enum.GetValues<BeaconQueueTrigger>())
        {
            if (trigger == BeaconQueueTrigger.AttemptFailed)
            {
                Assert.False(BeaconQueue.TryFailAttempt(status, retryBudgetSpent: false, out _));
                Assert.False(BeaconQueue.TryFailAttempt(status, retryBudgetSpent: true, out _));
                continue;
            }

            Assert.False(BeaconQueue.CanFire(status, trigger));
        }
    }

    [Theory]
    [InlineData(BeaconQueueStatus.Sent)]
    [InlineData(BeaconQueueStatus.Failed)]
    [InlineData(BeaconQueueStatus.Expired)]
    [InlineData(BeaconQueueStatus.Dropped)]
    public void TerminalBeacon_FireReportsTheIllegalTrigger(BeaconQueueStatus status)
    {
        Assert.Throws<InvalidOperationException>(
            () => BeaconQueue.Fire(status, BeaconQueueTrigger.Sent));
    }

    [Fact]
    public void TryFire_ReportsAnIllegalTriggerWithoutChangingTheState()
    {
        bool applied = BeaconQueue.TryFire(
            BeaconQueueStatus.Dropped,
            BeaconQueueTrigger.Sent,
            out BeaconQueueStatus next);

        Assert.False(applied);
        Assert.Equal(BeaconQueueStatus.Dropped, next);
    }
}

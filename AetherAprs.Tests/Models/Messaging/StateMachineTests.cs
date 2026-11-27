// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Messaging;
using AetherAprs.Services.Ports;
using System;
using Xunit;

namespace AetherAprs.Tests.Models.Messaging;

public sealed class MessageDeliveryTests
{
    [Theory]
    [InlineData(MessageDeliveryStatus.Queued)]
    [InlineData(MessageDeliveryStatus.Pending)]
    public void OpenStates_AreNotTerminal(MessageDeliveryStatus status)
    {
        Assert.True(MessageDelivery.IsOpen(status));
        Assert.False(MessageDelivery.IsTerminal(status));
    }

    [Theory]
    [InlineData(MessageDeliveryStatus.Acknowledged)]
    [InlineData(MessageDeliveryStatus.Rejected)]
    [InlineData(MessageDeliveryStatus.Timeout)]
    [InlineData(MessageDeliveryStatus.Failed)]
    [InlineData(MessageDeliveryStatus.Expired)]
    public void ResolvedStates_AreTerminal(MessageDeliveryStatus status)
    {
        Assert.False(MessageDelivery.IsOpen(status));
        Assert.True(MessageDelivery.IsTerminal(status));
    }

    [Fact]
    public void Sending_IsNeitherOpenNorTerminal()
    {
        // The durable record stays Pending while the wire is in use, so Sending is an
        // in-flight hint rather than a state the outbox ever persists.
        Assert.False(MessageDelivery.IsOpen(MessageDeliveryStatus.Sending));
        Assert.False(MessageDelivery.IsTerminal(MessageDeliveryStatus.Sending));
    }

    [Fact]
    public void QueuedMessage_ReservesANumberAndBecomesPending()
    {
        Assert.Equal(
            MessageDeliveryStatus.Pending,
            MessageDelivery.Fire(MessageDeliveryStatus.Queued, DeliveryTrigger.ReserveWireNumber));
    }

    [Theory]
    [InlineData(DeliveryTrigger.Acknowledge, MessageDeliveryStatus.Acknowledged)]
    [InlineData(DeliveryTrigger.Reject, MessageDeliveryStatus.Rejected)]
    [InlineData(DeliveryTrigger.Timeout, MessageDeliveryStatus.Timeout)]
    [InlineData(DeliveryTrigger.Fail, MessageDeliveryStatus.Failed)]
    [InlineData(DeliveryTrigger.Expire, MessageDeliveryStatus.Expired)]
    public void PendingMessage_ReachesEveryTerminalOutcome(
        DeliveryTrigger trigger,
        MessageDeliveryStatus expected)
    {
        Assert.Equal(
            expected,
            MessageDelivery.Fire(MessageDeliveryStatus.Pending, trigger));
    }

    [Theory]
    [InlineData(DeliveryTrigger.Acknowledge, MessageDeliveryStatus.Acknowledged)]
    [InlineData(DeliveryTrigger.Reject, MessageDeliveryStatus.Rejected)]
    [InlineData(DeliveryTrigger.Timeout, MessageDeliveryStatus.Timeout)]
    [InlineData(DeliveryTrigger.Fail, MessageDeliveryStatus.Failed)]
    [InlineData(DeliveryTrigger.Expire, MessageDeliveryStatus.Expired)]
    public void InFlightMessage_ReachesEveryTerminalOutcome(
        DeliveryTrigger trigger,
        MessageDeliveryStatus expected)
    {
        // An ACK can arrive while the attempt is still writing to the port.
        Assert.Equal(
            expected,
            MessageDelivery.Fire(MessageDeliveryStatus.Sending, trigger));
    }

    [Fact]
    public void Attempt_ReturnsToPendingWhenItEndsWithoutAnOutcome()
    {
        Assert.Equal(
            MessageDeliveryStatus.Pending,
            MessageDelivery.Fire(MessageDeliveryStatus.Sending, DeliveryTrigger.EndAttempt));
    }

    [Theory]
    [InlineData(MessageDeliveryStatus.Acknowledged)]
    [InlineData(MessageDeliveryStatus.Rejected)]
    [InlineData(MessageDeliveryStatus.Timeout)]
    [InlineData(MessageDeliveryStatus.Failed)]
    [InlineData(MessageDeliveryStatus.Expired)]
    public void TerminalMessage_RejectsEveryTrigger(MessageDeliveryStatus status)
    {
        // A duplicate ACK or a late retry deadline must never resurrect a message that
        // already resolved.
        foreach (DeliveryTrigger trigger in Enum.GetValues<DeliveryTrigger>())
        {
            Assert.False(MessageDelivery.CanFire(status, trigger));
            Assert.Throws<InvalidOperationException>(() => MessageDelivery.Fire(status, trigger));
        }
    }

    [Fact]
    public void TryFire_ReportsAnIllegalTriggerWithoutChangingTheState()
    {
        bool applied = MessageDelivery.TryFire(
            MessageDeliveryStatus.Acknowledged,
            DeliveryTrigger.Timeout,
            out MessageDeliveryStatus next);

        Assert.False(applied);
        Assert.Equal(MessageDeliveryStatus.Acknowledged, next);
    }

    [Fact]
    public void TryFire_ReportsALegalTriggerWithItsDestination()
    {
        bool applied = MessageDelivery.TryFire(
            MessageDeliveryStatus.Pending,
            DeliveryTrigger.Acknowledge,
            out MessageDeliveryStatus next);

        Assert.True(applied);
        Assert.Equal(MessageDeliveryStatus.Acknowledged, next);
    }

    [Theory]
    [InlineData(MessageDeliveryStatus.Acknowledged, DeliveryTrigger.Acknowledge)]
    [InlineData(MessageDeliveryStatus.Rejected, DeliveryTrigger.Reject)]
    [InlineData(MessageDeliveryStatus.Timeout, DeliveryTrigger.Timeout)]
    [InlineData(MessageDeliveryStatus.Failed, DeliveryTrigger.Fail)]
    [InlineData(MessageDeliveryStatus.Expired, DeliveryTrigger.Expire)]
    public void TriggerForOutcome_RoundTripsEveryTerminalStatus(
        MessageDeliveryStatus outcome,
        DeliveryTrigger expected)
    {
        Assert.Equal(expected, MessageDelivery.TriggerForOutcome(outcome));
    }

    [Theory]
    [InlineData(MessageDeliveryStatus.Queued)]
    [InlineData(MessageDeliveryStatus.Pending)]
    [InlineData(MessageDeliveryStatus.Sending)]
    public void TriggerForOutcome_RejectsAStatusThatIsNotAnOutcome(MessageDeliveryStatus status)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MessageDelivery.TriggerForOutcome(status));
    }
}

public sealed class PortSessionTests
{
    [Theory]
    [InlineData(PortSessionState.Connecting)]
    [InlineData(PortSessionState.Connected)]
    [InlineData(PortSessionState.Degraded)]
    public void WorkingSessions_AreLive(PortSessionState state) => Assert.True(PortSession.IsLive(state));

    [Theory]
    [InlineData(PortSessionState.Stopped)]
    public void InertSessions_AreNotLive(PortSessionState state) => Assert.False(PortSession.IsLive(state));

    [Fact]
    public void StoppedSession_Connects()
    {
        Assert.Equal(
            PortSessionState.Connecting,
            PortSession.Fire(PortSessionState.Stopped, PortSessionTrigger.Connect));
    }

    [Fact]
    public void ConnectionAttempt_ThatSucceeds_BecomesConnected()
    {
        Assert.Equal(
            PortSessionState.Connected,
            PortSession.Fire(PortSessionState.Connecting, PortSessionTrigger.Connected));
    }

    [Fact]
    public void ConnectionAttempt_ThatFails_BecomesDegraded()
    {
        Assert.Equal(
            PortSessionState.Degraded,
            PortSession.Fire(PortSessionState.Connecting, PortSessionTrigger.Faulted));
    }

    [Fact]
    public void DegradedSession_RetriesByConnectingAgain()
    {
        Assert.Equal(
            PortSessionState.Connecting,
            PortSession.Fire(PortSessionState.Degraded, PortSessionTrigger.Connect));
    }

    [Fact]
    public void LiveSession_ThatBreaks_BecomesDegraded()
    {
        Assert.Equal(
            PortSessionState.Degraded,
            PortSession.Fire(PortSessionState.Connected, PortSessionTrigger.Faulted));
    }

    [Theory]
    [InlineData(PortSessionState.Connecting)]
    [InlineData(PortSessionState.Connected)]
    [InlineData(PortSessionState.Degraded)]
    public void AnyLiveSession_CanBeTornDown(PortSessionState state)
    {
        Assert.Equal(
            PortSessionState.Stopped,
            PortSession.Fire(state, PortSessionTrigger.Disconnect));
    }

    [Fact]
    public void StoppedSession_RejectsALateFault()
    {
        // A transport callback from a modem teardown already released must not
        // resurrect the session.
        Assert.False(PortSession.CanFire(PortSessionState.Stopped, PortSessionTrigger.Faulted));
        Assert.False(PortSession.TryFire(
            PortSessionState.Stopped,
            PortSessionTrigger.Faulted,
            out PortSessionState next));
        Assert.Equal(PortSessionState.Stopped, next);
    }

    [Fact]
    public void StoppedSession_RejectsADuplicateDisconnect()
    {
        Assert.False(PortSession.CanFire(PortSessionState.Stopped, PortSessionTrigger.Disconnect));
    }

    [Fact]
    public void StoppedSession_RejectsASuccessfulConnection()
    {
        // Only the reconnect loop opens a transport, and it always connects first.
        Assert.False(PortSession.CanFire(PortSessionState.Stopped, PortSessionTrigger.Connected));
    }

    [Fact]
    public void TryFire_ReportsALegalTriggerWithItsDestination()
    {
        bool applied = PortSession.TryFire(
            PortSessionState.Connecting,
            PortSessionTrigger.Connected,
            out PortSessionState next);

        Assert.True(applied);
        Assert.Equal(PortSessionState.Connected, next);
    }
}

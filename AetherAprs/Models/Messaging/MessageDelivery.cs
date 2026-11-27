// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Stateless;
using System;

namespace AetherAprs.Models.Messaging;

/// <summary>
/// The events that move an outbound message through its delivery lifecycle.
/// </summary>
/// <remarks>
/// Every member names an intent rather than a destination, so a caller cannot
/// record a delivery state the protocol does not allow. The mapping from trigger
/// to destination is declared once in <see cref="MessageDelivery"/>.
/// </remarks>
public enum DeliveryTrigger
{
    /// <summary>A free wire number was reserved, so the message may be transmitted.</summary>
    ReserveWireNumber,

    /// <summary>A transmission attempt is using the wire.</summary>
    BeginAttempt,

    /// <summary>The attempt finished without an outcome yet, so the message waits again.</summary>
    EndAttempt,

    /// <summary>The addressee acknowledged the message.</summary>
    Acknowledge,

    /// <summary>The addressee rejected the message.</summary>
    Reject,

    /// <summary>The retry budget was spent without an acknowledgment.</summary>
    Timeout,

    /// <summary>Transmission failed in a way that will not succeed on a retry.</summary>
    Fail,

    /// <summary>The message passed its deadline before it was delivered.</summary>
    Expire,
}

/// <summary>
/// The delivery lifecycle of an outbound APRS message: which states exist, which
/// transitions are legal, and which states are terminal.
/// </summary>
/// <remarks>
/// <para>
/// The durable record stores the state, so the table has to be readable on its own.
/// Declaring it here means the "a terminal message never goes back to pending" rule
/// is enforced by construction instead of by every caller remembering to re-check
/// <see cref="IsOpen"/> before writing.
/// </para>
/// <para>
/// The type is stateless: <see cref="Fire"/> takes the current state and returns the
/// next one. Callers already hold the state they are transitioning from, so a
/// long-lived machine object would duplicate it and invite the two copies to drift.
/// </para>
/// </remarks>
public static class MessageDelivery
{
    /// <summary>
    /// Whether the message still has work to do. Open messages are the ones the
    /// retry loop and the expiry sweep consider.
    /// </summary>
    public static bool IsOpen(MessageDeliveryStatus status) =>
        status is MessageDeliveryStatus.Queued or MessageDeliveryStatus.Pending;

    /// <summary>
    /// Whether the message reached a state it can never leave, i.e. an outcome was
    /// recorded and no further attempt will be made.
    /// </summary>
    /// <remarks>
    /// <see cref="MessageDeliveryStatus.Sending"/> is deliberately neither open nor
    /// terminal: the wire is in use, and the durable record is still
    /// <see cref="MessageDeliveryStatus.Pending"/> underneath.
    /// </remarks>
    public static bool IsTerminal(MessageDeliveryStatus status) =>
        status is MessageDeliveryStatus.Acknowledged
            or MessageDeliveryStatus.Rejected
            or MessageDeliveryStatus.Timeout
            or MessageDeliveryStatus.Failed
            or MessageDeliveryStatus.Expired;

    /// <summary>Whether <paramref name="trigger"/> is legal from <paramref name="status"/>.</summary>
    public static bool CanFire(MessageDeliveryStatus status, DeliveryTrigger trigger) =>
        CreateMachine(status).CanFire(trigger);

    /// <summary>
    /// Applies <paramref name="trigger"/> to <paramref name="status"/>.
    /// </summary>
    /// <param name="status">The state the message is in.</param>
    /// <param name="trigger">The event being recorded.</param>
    /// <returns>The state the message moves to.</returns>
    /// <exception cref="InvalidOperationException">
    /// The trigger is not legal from <paramref name="status"/>. Callers that accept
    /// an out-of-order event (a late ACK, a concurrent outcome) must ask
    /// <see cref="CanFire"/> first and treat a false result as "already resolved".
    /// </exception>
    public static MessageDeliveryStatus Fire(MessageDeliveryStatus status, DeliveryTrigger trigger)
    {
        StateMachine<MessageDeliveryStatus, DeliveryTrigger> machine = CreateMachine(status);
        machine.Fire(trigger);
        return machine.State;
    }

    /// <summary>
    /// Applies <paramref name="trigger"/> when it is legal, and reports whether it was.
    /// </summary>
    /// <param name="status">The state the message is in.</param>
    /// <param name="trigger">The event being recorded.</param>
    /// <param name="next">The resulting state, or <paramref name="status"/> when the trigger is illegal.</param>
    /// <returns><see langword="true"/> when the transition was applied.</returns>
    public static bool TryFire(
        MessageDeliveryStatus status,
        DeliveryTrigger trigger,
        out MessageDeliveryStatus next)
    {
        StateMachine<MessageDeliveryStatus, DeliveryTrigger> machine = CreateMachine(status);
        if (!machine.CanFire(trigger))
        {
            next = status;
            return false;
        }

        machine.Fire(trigger);
        next = machine.State;
        return true;
    }

    /// <summary>
    /// Maps a recorded delivery outcome onto the trigger that produces it.
    /// </summary>
    /// <param name="outcome">The outcome to record.</param>
    /// <returns>The trigger that reaches <paramref name="outcome"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="outcome"/> is not a terminal delivery outcome.
    /// </exception>
    public static DeliveryTrigger TriggerForOutcome(MessageDeliveryStatus outcome) =>
        outcome switch
        {
            MessageDeliveryStatus.Acknowledged => DeliveryTrigger.Acknowledge,
            MessageDeliveryStatus.Rejected => DeliveryTrigger.Reject,
            MessageDeliveryStatus.Timeout => DeliveryTrigger.Timeout,
            MessageDeliveryStatus.Failed => DeliveryTrigger.Fail,
            MessageDeliveryStatus.Expired => DeliveryTrigger.Expire,
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A delivery outcome must be terminal.")
        };

    /// <summary>
    /// Declares the transition table for one message. Building it per call keeps the
    /// type free of shared mutable state; the paths that use it are driven by timers
    /// and terminal outcomes, not by inbound packet volume.
    /// </summary>
    private static StateMachine<MessageDeliveryStatus, DeliveryTrigger> CreateMachine(MessageDeliveryStatus status)
    {
        StateMachine<MessageDeliveryStatus, DeliveryTrigger> machine = new(status);

        // Waiting for a free wire number. Only expiry can end it without one.
        _ = machine.Configure(MessageDeliveryStatus.Queued)
            .Permit(DeliveryTrigger.ReserveWireNumber, MessageDeliveryStatus.Pending)
            .Permit(DeliveryTrigger.Expire, MessageDeliveryStatus.Expired);

        // A number is held. Every outcome is legal from here, including expiry.
        _ = machine.Configure(MessageDeliveryStatus.Pending)
            .Permit(DeliveryTrigger.BeginAttempt, MessageDeliveryStatus.Sending)
            .Permit(DeliveryTrigger.Acknowledge, MessageDeliveryStatus.Acknowledged)
            .Permit(DeliveryTrigger.Reject, MessageDeliveryStatus.Rejected)
            .Permit(DeliveryTrigger.Timeout, MessageDeliveryStatus.Timeout)
            .Permit(DeliveryTrigger.Fail, MessageDeliveryStatus.Failed)
            .Permit(DeliveryTrigger.Expire, MessageDeliveryStatus.Expired);

        // The wire is in use. An outcome can still arrive while the attempt runs,
        // so the terminal transitions stay available.
        _ = machine.Configure(MessageDeliveryStatus.Sending)
            .Permit(DeliveryTrigger.EndAttempt, MessageDeliveryStatus.Pending)
            .Permit(DeliveryTrigger.Acknowledge, MessageDeliveryStatus.Acknowledged)
            .Permit(DeliveryTrigger.Reject, MessageDeliveryStatus.Rejected)
            .Permit(DeliveryTrigger.Timeout, MessageDeliveryStatus.Timeout)
            .Permit(DeliveryTrigger.Fail, MessageDeliveryStatus.Failed)
            .Permit(DeliveryTrigger.Expire, MessageDeliveryStatus.Expired);

        // Terminal states accept nothing: a late or duplicate event cannot resurrect
        // a message that already resolved.
        _ = machine.Configure(MessageDeliveryStatus.Acknowledged);
        _ = machine.Configure(MessageDeliveryStatus.Rejected);
        _ = machine.Configure(MessageDeliveryStatus.Timeout);
        _ = machine.Configure(MessageDeliveryStatus.Failed);
        _ = machine.Configure(MessageDeliveryStatus.Expired);

        return machine;
    }
}

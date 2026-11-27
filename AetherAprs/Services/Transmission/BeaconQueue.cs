// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Stateless;
using System;

namespace AetherAprs.Services.Transmission;

/// <summary>
/// The events that move a queued beacon toward the modem.
/// </summary>
public enum BeaconQueueTrigger
{
    /// <summary>The beacon's turn on the wire arrived.</summary>
    BeginSending,

    /// <summary>The modem accepted the beacon.</summary>
    Sent,

    /// <summary>The beacon aged out before it was transmitted.</summary>
    Expire,

    /// <summary>Queue capacity forced the beacon out to admit a newer one.</summary>
    Drop,

    /// <summary>An attempt did not reach the modem.</summary>
    AttemptFailed,

    /// <summary>The beacon returns to the queue for another attempt.</summary>
    Retry,
}

/// <summary>
/// The lifecycle of one queued beacon: which states exist, which transitions are
/// legal, and which states retain no further work.
/// </summary>
/// <remarks>
/// <para>
/// A failed attempt either retries in place or reaches a terminal failure, decided by
/// the attempt budget and the expiry deadline. Declaring that choice here keeps the
/// store from re-deriving it, and makes the four terminal states absorbing so a late
/// callback cannot resurrect a beacon that was already dropped or expired.
/// </para>
/// <para>
/// The type is stateless: <see cref="Fire"/> takes the current state and returns the
/// next one, because the durable row already stores the state being transitioned from.
/// </para>
/// </remarks>
public static class BeaconQueue
{
    /// <summary>Whether the beacon still has work left, i.e. it holds a queue slot.</summary>
    public static bool IsOpen(BeaconQueueStatus status) =>
        status is BeaconQueueStatus.Queued or BeaconQueueStatus.Sending;

    /// <summary>Whether the beacon reached a state it can never leave.</summary>
    public static bool IsTerminal(BeaconQueueStatus status) => !IsOpen(status);

    /// <summary>Whether <paramref name="trigger"/> is legal from <paramref name="status"/>.</summary>
    public static bool CanFire(BeaconQueueStatus status, BeaconQueueTrigger trigger) =>
        CreateMachine(status, retryBudgetSpent: false).CanFire(trigger);

    /// <summary>
    /// Applies <paramref name="trigger"/> to <paramref name="status"/>.
    /// </summary>
    /// <param name="status">The state the beacon is in.</param>
    /// <param name="trigger">The event being recorded.</param>
    /// <returns>The state the beacon moves to.</returns>
    /// <exception cref="InvalidOperationException">
    /// The trigger is not legal from <paramref name="status"/>. Callers that accept an
    /// out-of-order event must ask <see cref="CanFire"/> first.
    /// </exception>
    /// <remarks>
    /// <see cref="BeaconQueueTrigger.AttemptFailed"/> is not handled here: its
    /// destination depends on the retry budget, so it goes through
    /// <see cref="TryFailAttempt"/>.
    /// </remarks>
    public static BeaconQueueStatus Fire(BeaconQueueStatus status, BeaconQueueTrigger trigger)
    {
        StateMachine<BeaconQueueStatus, BeaconQueueTrigger> machine =
            CreateMachine(status, retryBudgetSpent: false);
        machine.Fire(trigger);
        return machine.State;
    }

    /// <summary>
    /// Applies <paramref name="trigger"/> when it is legal, and reports whether it was.
    /// </summary>
    /// <param name="status">The state the beacon is in.</param>
    /// <param name="trigger">The event being recorded.</param>
    /// <param name="next">The resulting state, or <paramref name="status"/> when the trigger is illegal.</param>
    /// <returns><see langword="true"/> when the transition was applied.</returns>
    public static bool TryFire(
        BeaconQueueStatus status,
        BeaconQueueTrigger trigger,
        out BeaconQueueStatus next)
    {
        StateMachine<BeaconQueueStatus, BeaconQueueTrigger> machine =
            CreateMachine(status, retryBudgetSpent: false);
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
    /// Records a failed attempt, which retries in place while the budget lasts and
    /// reaches the terminal failure state once it is spent.
    /// </summary>
    /// <param name="status">The state the beacon is in.</param>
    /// <param name="retryBudgetSpent">
    /// Whether the attempt budget is exhausted or the beacon is past its expiry. The
    /// caller owns the budget; the table owns what "spent" means for the lifecycle.
    /// </param>
    /// <param name="next">
    /// The resulting state: <see langword="false"/> leaves
    /// <see cref="BeaconQueueStatus.Queued"/> as
    /// <see cref="BeaconQueueStatus.Queued"/> and <see cref="BeaconQueueStatus.Sending"/>
    /// as <see cref="BeaconQueueStatus.Queued"/>; <see langword="true"/> always reaches
    /// <see cref="BeaconQueueStatus.Failed"/>.
    /// </param>
    /// <returns><see langword="true"/> when the attempt was recorded.</returns>
    public static bool TryFailAttempt(
        BeaconQueueStatus status,
        bool retryBudgetSpent,
        out BeaconQueueStatus next)
    {
        if (IsTerminal(status))
        {
            next = status;
            return false;
        }

        if (status == BeaconQueueStatus.Queued && !retryBudgetSpent)
        {
            // The beacon never left the queue, so a failed attempt is not a
            // transition: it keeps its slot and only its attempt count moves.
            next = BeaconQueueStatus.Queued;
            return true;
        }

        StateMachine<BeaconQueueStatus, BeaconQueueTrigger> machine =
            CreateMachine(status, retryBudgetSpent);
        if (!machine.CanFire(BeaconQueueTrigger.AttemptFailed))
        {
            next = status;
            return false;
        }

        machine.Fire(BeaconQueueTrigger.AttemptFailed);
        next = machine.State;
        return true;
    }

    /// <summary>
    /// Declares the transition table for one beacon. Building it per call keeps the
    /// type free of shared mutable state; transitions happen once per attempt, not per
    /// inbound packet.
    /// </summary>
    /// <param name="status">The state the beacon is in.</param>
    /// <param name="retryBudgetSpent">Whether a failed attempt must be terminal.</param>
    private static StateMachine<BeaconQueueStatus, BeaconQueueTrigger> CreateMachine(
        BeaconQueueStatus status,
        bool retryBudgetSpent)
    {
        StateMachine<BeaconQueueStatus, BeaconQueueTrigger> machine = new(status);

        // Waiting for the wire. Any terminal outcome is reachable from here, and a
        // failed attempt retries in place: the beacon keeps its slot and its place in
        // the queue order.
        _ = machine.Configure(BeaconQueueStatus.Queued)
            .Permit(BeaconQueueTrigger.BeginSending, BeaconQueueStatus.Sending)
            .Permit(BeaconQueueTrigger.Sent, BeaconQueueStatus.Sent)
            .Permit(BeaconQueueTrigger.Expire, BeaconQueueStatus.Expired)
            .Permit(BeaconQueueTrigger.Drop, BeaconQueueStatus.Dropped)
            .PermitIf(
                BeaconQueueTrigger.AttemptFailed,
                BeaconQueueStatus.Failed,
                () => retryBudgetSpent,
                "the attempt budget is spent or the beacon expired");

        // On the wire. A failed attempt returns the beacon to the queue so the
        // reconciler can pick it up again, unless the budget is spent. The two
        // branches are mutually exclusive because Stateless requires it.
        _ = machine.Configure(BeaconQueueStatus.Sending)
            .Permit(BeaconQueueTrigger.Sent, BeaconQueueStatus.Sent)
            .Permit(BeaconQueueTrigger.Expire, BeaconQueueStatus.Expired)
            .Permit(BeaconQueueTrigger.Retry, BeaconQueueStatus.Queued)
            .PermitIf(
                BeaconQueueTrigger.AttemptFailed,
                BeaconQueueStatus.Failed,
                () => retryBudgetSpent,
                "the attempt budget is spent or the beacon expired")
            .PermitIf(
                BeaconQueueTrigger.AttemptFailed,
                BeaconQueueStatus.Queued,
                () => !retryBudgetSpent,
                "the retry budget still has attempts");

        // Terminal states accept nothing: a late send callback cannot resurrect a
        // beacon that was dropped for capacity or already expired.
        _ = machine.Configure(BeaconQueueStatus.Sent);
        _ = machine.Configure(BeaconQueueStatus.Expired);
        _ = machine.Configure(BeaconQueueStatus.Failed);
        _ = machine.Configure(BeaconQueueStatus.Dropped);

        return machine;
    }
}

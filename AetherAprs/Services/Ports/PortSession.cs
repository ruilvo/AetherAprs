// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using Stateless;

namespace AetherAprs.Services.Ports;

/// <summary>
/// The events that move one port's session through connect and retry.
/// </summary>
public enum PortSessionTrigger
{
    /// <summary>An attempt to open the transport begins.</summary>
    Connect,

    /// <summary>The transport opened.</summary>
    Connected,

    /// <summary>An attempt failed, or a live transport broke.</summary>
    Faulted,

    /// <summary>The session is being torn down.</summary>
    Disconnect,
}

/// <summary>
/// The lifecycle of one port's session: which states exist, which transitions are
/// legal, and which are absorbing.
/// </summary>
/// <remarks>
/// <para>
/// The transitions are driven from several places — the start path, the reconnect
/// loop, the transport fault callback and teardown — and each of them has to know
/// whether the session may still make progress. Declaring the table once keeps a
/// late transport callback from resurrecting a session that teardown already
/// stopped, which used to be re-checked by hand at every call site.
/// </para>
/// <para>
/// The type is stateless: <see cref="Fire"/> takes the current state and returns the
/// next one. The coordinator already stores the state next to the snapshot it
/// publishes, so a machine object would only duplicate it.
/// </para>
/// </remarks>
public static class PortSession
{
    /// <summary>Whether the session is working toward a connection or holding one.</summary>
    public static bool IsLive(PortSessionState state) =>
        state is PortSessionState.Connecting
            or PortSessionState.Connected
            or PortSessionState.Degraded;

    /// <summary>Whether <paramref name="trigger"/> is legal from <paramref name="state"/>.</summary>
    public static bool CanFire(PortSessionState state, PortSessionTrigger trigger) =>
        CreateMachine(state).CanFire(trigger);

    /// <summary>
    /// Applies <paramref name="trigger"/> to <paramref name="state"/>.
    /// </summary>
    /// <param name="state">The state the session is in.</param>
    /// <param name="trigger">The event being recorded.</param>
    /// <returns>The state the session moves to.</returns>
    /// <exception cref="InvalidOperationException">
    /// The trigger is not legal from <paramref name="state"/>. Callers that accept an
    /// out-of-order event must ask <see cref="CanFire"/> first.
    /// </exception>
    public static PortSessionState Fire(PortSessionState state, PortSessionTrigger trigger)
    {
        StateMachine<PortSessionState, PortSessionTrigger> machine = CreateMachine(state);
        machine.Fire(trigger);
        return machine.State;
    }

    /// <summary>
    /// Applies <paramref name="trigger"/> when it is legal, and reports whether it was.
    /// </summary>
    /// <param name="state">The state the session is in.</param>
    /// <param name="trigger">The event being recorded.</param>
    /// <param name="next">The resulting state, or <paramref name="state"/> when the trigger is illegal.</param>
    /// <returns><see langword="true"/> when the transition was applied.</returns>
    public static bool TryFire(
        PortSessionState state,
        PortSessionTrigger trigger,
        out PortSessionState next)
    {
        StateMachine<PortSessionState, PortSessionTrigger> machine = CreateMachine(state);
        if (!machine.CanFire(trigger))
        {
            next = state;
            return false;
        }

        machine.Fire(trigger);
        next = machine.State;
        return true;
    }

    /// <summary>
    /// Declares the transition table for one session. Building it per call keeps the
    /// type free of shared mutable state; transitions happen on reconnect and
    /// teardown, not per inbound packet.
    /// </summary>
    private static StateMachine<PortSessionState, PortSessionTrigger> CreateMachine(PortSessionState state)
    {
        StateMachine<PortSessionState, PortSessionTrigger> machine = new(state);

        // A stopped session cannot receive a fault: teardown owns the remaining
        // transition, and a late callback must not reopen the session.
        _ = machine.Configure(PortSessionState.Stopped)
            .Permit(PortSessionTrigger.Connect, PortSessionState.Connecting);

        // An attempt is in flight and can fail or succeed.
        _ = machine.Configure(PortSessionState.Connecting)
            .Permit(PortSessionTrigger.Connected, PortSessionState.Connected)
            .Permit(PortSessionTrigger.Faulted, PortSessionState.Degraded)
            .Permit(PortSessionTrigger.Disconnect, PortSessionState.Stopped);

        // The transport is up; a break degrades it and teardown stops it.
        _ = machine.Configure(PortSessionState.Connected)
            .Permit(PortSessionTrigger.Faulted, PortSessionState.Degraded)
            .Permit(PortSessionTrigger.Disconnect, PortSessionState.Stopped);

        // The last attempt failed. The reconnect loop tries again, which is what
        // separates a degraded session from a stopped one.
        _ = machine.Configure(PortSessionState.Degraded)
            .Permit(PortSessionTrigger.Connect, PortSessionState.Connecting)
            .Permit(PortSessionTrigger.Disconnect, PortSessionState.Stopped);

        return machine;
    }
}

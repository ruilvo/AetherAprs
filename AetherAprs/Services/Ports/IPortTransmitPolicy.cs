// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs.Packets;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Outcome of one transmit attempt through <see cref="IPortTransmitPolicy"/>.
/// <paramref name="Status"/> uses the session manager's send status so a caller
/// reports the same "sent" or "not running" verdict whichever producer asked;
/// <paramref name="StartedSession"/> tells the port façade whether the attempt
/// changed the running set, and <paramref name="Error"/> carries the modem failure
/// when a start was attempted and refused.
/// </summary>
public sealed record PortTransmitOutcome(
    PortSessionSendStatus Status,
    bool StartedSession,
    Exception? Error = null);

/// <summary>
/// The one place outbound packets reach a modem. Every producer — beaconing,
/// messaging, objects and the digipeat relay — asks this policy instead of the
/// session manager, so transport readiness, the operator's persisted enablement and
/// the running-session recheck are applied identically to all of them.
/// </summary>
/// <remarks>
/// A forward never brings a port up: only the operator's persisted enablement may
/// do that. A caller that is not allowed an implicit start therefore learns
/// <see cref="PortSessionSendStatus.NotRunning"/> instead of a started port.
/// </remarks>
public interface IPortTransmitPolicy
{
    /// <summary>
    /// Sends a packet through the port's running session.
    /// </summary>
    /// <param name="portId">The port to transmit on.</param>
    /// <param name="packet">The packet to transmit.</param>
    /// <param name="allowImplicitStart">
    /// Whether an enabled port may be started when no session is running. Producers
    /// that own operator intent pass <see langword="true"/>; the forwarding relay
    /// passes <see langword="false"/> so it can never start a stopped port.
    /// </param>
    /// <param name="cancellationToken">Cancels the send and a possible start.</param>
    Task<PortTransmitOutcome> SendAsync(
        Guid portId,
        AprsPacket packet,
        bool allowImplicitStart,
        CancellationToken cancellationToken = default);
}

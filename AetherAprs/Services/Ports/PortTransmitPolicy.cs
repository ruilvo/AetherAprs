// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration;
using AetherAprs.Models.Aprs.Packets;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Applies the shared transmit policy over the configured ports and the owner of
/// running sessions. It deliberately depends on neither <see cref="PortService"/>
/// nor <see cref="IDigipeatRouter"/>, so both can use it without forming a cycle.
/// </summary>
internal sealed class PortTransmitPolicy : IPortTransmitPolicy
{
    private readonly IPortConfigurationSource _configurationSource;
    private readonly IPortSessionManager _sessionManager;

    public PortTransmitPolicy(
        IPortConfigurationSource configurationSource,
        IPortSessionManager sessionManager)
    {
        ArgumentNullException.ThrowIfNull(configurationSource);
        ArgumentNullException.ThrowIfNull(sessionManager);

        _configurationSource = configurationSource;
        _sessionManager = sessionManager;
    }

    /// <inheritdoc />
    public async Task<PortTransmitOutcome> SendAsync(
        Guid portId,
        AprsPacket packet,
        bool allowImplicitStart,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        // Fast path: a running session takes the packet without consulting intent.
        if (await _sessionManager
                .SendAsync(portId, packet, cancellationToken)
                .ConfigureAwait(false) == PortSessionSendStatus.Sent)
        {
            return new PortTransmitOutcome(PortSessionSendStatus.Sent, StartedSession: false);
        }

        // Nothing is running. Read the operator's persisted enablement now rather
        // than trusting an earlier observation: it is the arm intent that must
        // still hold at the moment this packet would key a transmitter.
        PortConfig? port = _configurationSource.Find(portId);
        if (!allowImplicitStart || port is null || !port.IsEnabled)
        {
            return new PortTransmitOutcome(PortSessionSendStatus.NotRunning, StartedSession: false);
        }

        PortSessionStartResult start = await _sessionManager
            .StartAsync(port, cancellationToken)
            .ConfigureAwait(false);
        if (!start.IsRunning)
        {
            return new PortTransmitOutcome(PortSessionSendStatus.NotRunning, StartedSession: false, start.Error);
        }

        if (await _sessionManager
                .SendAsync(portId, packet, cancellationToken)
                .ConfigureAwait(false) != PortSessionSendStatus.Sent)
        {
            return new PortTransmitOutcome(PortSessionSendStatus.NotRunning, StartedSession: true);
        }

        return new PortTransmitOutcome(PortSessionSendStatus.Sent, StartedSession: true);
    }
}

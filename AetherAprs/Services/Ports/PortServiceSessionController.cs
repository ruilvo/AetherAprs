// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Runtime-only modem session control, deliberately separate from the persisted
/// user intent exposed by <see cref="IPortService.SetPortEnabledAsync"/>.
/// </summary>
/// <remarks>
/// Supervision (reconnect after a transport fault) must never rewrite the port
/// configuration. <see cref="IPortService.SetPortEnabledAsync"/> persists
/// <see cref="AetherAprs.Configuration.PortConfig.IsEnabled"/>; these members only start and
/// stop the modem.
/// </remarks>
public interface IPortSessionRuntime
{
    /// <summary>
    /// Starts the modem for a port without changing its persisted
    /// <see cref="AetherAprs.Configuration.PortConfig.IsEnabled"/> value.
    /// </summary>
    /// <returns>
    /// The structured outcome, carrying the original failure so supervision can
    /// distinguish a cancelled connect from a rejected one.
    /// </returns>
    Task<PortSessionStartResult> StartSessionAsync(Guid portId, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the modem for a port without changing its persisted
    /// <see cref="AetherAprs.Configuration.PortConfig.IsEnabled"/> value.
    /// </summary>
    Task<PortSessionStopResult> StopSessionAsync(Guid portId, CancellationToken cancellationToken);
}

/// <summary>
/// Adapts the port service to the coordinator lifecycle contract using the
/// runtime-only session operations, so supervision never mutates user intent.
/// </summary>
public sealed class PortServiceSessionController(IPortSessionRuntime sessionRuntime) : ISessionPortController
{
    public async Task ConnectAsync(Guid portId, CancellationToken cancellationToken)
    {
        PortSessionStartResult result = await sessionRuntime
            .StartSessionAsync(portId, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsRunning)
        {
            return;
        }

        // The coordinator decides whether to retry from what it observes here, so a
        // cancelled connect must surface as cancellation and every other failure as
        // an error that still carries the original exception.
        if (result.Status == PortSessionStartStatus.Cancelled && result.Error is not null)
        {
            ExceptionDispatchInfo.Capture(result.Error).Throw();
        }

        throw new InvalidOperationException(
            $"Port {portId} could not be started: {result.Status}.",
            result.Error);
    }

    public async Task DisconnectAsync(Guid portId, CancellationToken cancellationToken) =>
        // A failed stop has already been reported by the session manager and has
        // released the port, so the coordinator only needs to know the session is
        // gone. Cancellation still propagates because the caller asked to stop waiting.
        await sessionRuntime
            .StopSessionAsync(portId, cancellationToken)
            .ConfigureAwait(false);
}

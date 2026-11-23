// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
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
/// <see cref="Configuration.PortConfig.IsEnabled"/>; these members only start and
/// stop the modem (audit C-02, ADR-001).
/// </remarks>
public interface IPortSessionRuntime
{
    /// <summary>
    /// Starts the modem for a port without changing its persisted
    /// <see cref="Configuration.PortConfig.IsEnabled"/> value.
    /// </summary>
    /// <returns><see langword="true"/> when the port is running after the call.</returns>
    Task<bool> StartSessionAsync(Guid portId, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the modem for a port without changing its persisted
    /// <see cref="Configuration.PortConfig.IsEnabled"/> value.
    /// </summary>
    Task StopSessionAsync(Guid portId, CancellationToken cancellationToken);
}

/// <summary>
/// Adapts the port service to the coordinator lifecycle contract using the
/// runtime-only session operations, so supervision never mutates user intent.
/// </summary>
public sealed class PortServiceSessionController(IPortSessionRuntime sessionRuntime) : ISessionPortController
{
    public Task ConnectAsync(Guid portId, CancellationToken cancellationToken) =>
        sessionRuntime.StartSessionAsync(portId, cancellationToken);

    public Task DisconnectAsync(Guid portId, CancellationToken cancellationToken) =>
        sessionRuntime.StopSessionAsync(portId, cancellationToken);
}

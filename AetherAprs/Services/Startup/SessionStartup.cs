// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Services.Ports;

using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Startup;

/// <summary>
/// The last startup stage: restore the persisted session snapshot, arm the operator's
/// enabled ports and let the coordinator connect them.
/// </summary>
/// <remarks>
/// Arming is explicit. Ports left enabled in configuration describe what the operator
/// wants running; the coordinator only starts them when this stage arms the intent.
/// </remarks>
public sealed class SessionStartup
{
    private readonly IPortService _portService;
    private readonly SessionCoordinator _coordinator;
    private readonly ILogger<SessionStartup> _logger;

    /// <summary>
    /// Initializes a new session startup stage.
    /// </summary>
    /// <param name="portService">The port configuration façade.</param>
    /// <param name="coordinator">The session supervisor.</param>
    /// <param name="telemetryBridge">
    /// The telemetry bridge. It is a constructor dependency on purpose: creating it
    /// subscribes to transport events before any session can raise one.
    /// </param>
    /// <param name="logger">The logger for the startup decision.</param>
    public SessionStartup(
        IPortService portService,
        SessionCoordinator coordinator,
        SessionCoordinatorTelemetryBridge telemetryBridge,
        ILogger<SessionStartup> logger)
    {
        ArgumentNullException.ThrowIfNull(portService);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(telemetryBridge);
        ArgumentNullException.ThrowIfNull(logger);

        _portService = portService;
        _coordinator = coordinator;
        _logger = logger;
    }

    /// <summary>
    /// Restores the persisted snapshot and arms every port the operator left enabled.
    /// </summary>
    /// <param name="cancellationToken">Cancels startup during shutdown.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _coordinator.InitializeAsync(cancellationToken).ConfigureAwait(false);

        Guid[] enabledPortIds = [.. _portService.Ports
            .Where(port => port.IsEnabled)
            .Select(port => port.Id)];

        if (enabledPortIds.Length == 0)
        {
            _logger.LogInformation("No port is enabled, so the session starts disarmed.");
            return;
        }

        _logger.LogInformation("Arming the session for {PortCount} enabled port(s).", enabledPortIds.Length);
        await _coordinator.StartAsync(enabledPortIds, cancellationToken).ConfigureAwait(false);
    }
}

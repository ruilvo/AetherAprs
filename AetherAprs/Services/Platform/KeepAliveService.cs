// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Default <see cref="IKeepAliveService"/> implementation. Tracks the set of active
/// keep-alive reasons and drives the platform <see cref="IForegroundService"/> so it
/// runs exactly while at least one reason is active.
/// </summary>
public sealed class KeepAliveService : IKeepAliveService
{
    private readonly IForegroundService _foregroundService;
    private readonly ILogger<KeepAliveService> _logger;
    private readonly Lock _lock = new();
    private readonly HashSet<KeepAliveReason> _activeReasons = new();

    public KeepAliveService(
        IForegroundService foregroundService,
        ILogger<KeepAliveService> logger)
    {
        _foregroundService = foregroundService;
        _logger = logger;
    }

    public async Task AcquireAsync(KeepAliveReason reason)
    {
        bool start;
        bool refresh;
        ForegroundServiceType requiredTypes;
        lock (_lock)
        {
            var added = _activeReasons.Add(reason);
            start = added && _activeReasons.Count == 1;
            refresh = added && !start;
            requiredTypes = GetRequiredTypesLocked();
        }

        if (start)
        {
            await _foregroundService.StartAsync(requiredTypes).ConfigureAwait(false);
            _logger.LogInformation("Started keep-alive foreground service");
        }
        else if (refresh)
        {
            // A second reason became active while running (for example location
            // tracking after the ports started the service): the platform must
            // declare the additional service type.
            await _foregroundService.StartAsync(requiredTypes).ConfigureAwait(false);
            _logger.LogInformation("Updated keep-alive foreground service types");
        }
    }

    public async Task ReleaseAsync(KeepAliveReason reason)
    {
        bool stop;
        lock (_lock)
        {
            stop = _activeReasons.Remove(reason) && _activeReasons.Count == 0;
        }

        if (stop)
        {
            await _foregroundService.StopAsync().ConfigureAwait(false);
            _logger.LogInformation("Stopped keep-alive foreground service");
        }
    }

    /// <summary>
    /// Maps the active keep-alive reasons to the service types the platform must
    /// declare. Callers must hold <see cref="_lock"/>.
    /// </summary>
    private ForegroundServiceType GetRequiredTypesLocked()
    {
        var types = ForegroundServiceType.None;

        if (_activeReasons.Contains(KeepAliveReason.Ports))
        {
            types |= ForegroundServiceType.ConnectedDevice;
        }

        if (_activeReasons.Contains(KeepAliveReason.LocationTracking))
        {
            types |= ForegroundServiceType.Location;
        }

        return types;
    }
}

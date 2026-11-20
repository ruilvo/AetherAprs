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
        lock (_lock)
        {
            start = _activeReasons.Add(reason) && _activeReasons.Count == 1;
        }

        if (start)
        {
            await _foregroundService.StartAsync().ConfigureAwait(false);
            _logger.LogInformation("Started keep-alive foreground service");
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
}

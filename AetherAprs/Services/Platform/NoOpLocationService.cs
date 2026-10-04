// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// No-op location service for platforms without location support (desktop).
/// </summary>
public class NoOpLocationService : ILocationService
{
    public Task<LocationData> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Location services are not available on this platform.");
    }

    public bool IsLocationAvailable()
    {
        return false;
    }

    public Task<bool> RequestLocationPermissionAsync()
    {
        // Desktop platforms don't need location permission
        return Task.FromResult(false);
    }
}

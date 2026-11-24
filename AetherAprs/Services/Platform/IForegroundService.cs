// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Capabilities that a keep-alive holder requires from the platform foreground
/// service. Platform implementations map these to their own foreground service
/// types (for example Android's <c>connectedDevice</c> and <c>location</c>).
/// </summary>
[Flags]
public enum ForegroundServiceType
{
    /// <summary>
    /// No capability declared. Platform implementations should assume
    /// <see cref="ConnectedDevice"/>, because the service exists to hold the
    /// radio link.
    /// </summary>
    None = 0,

    /// <summary>A long-lived connection to an external device such as a radio.</summary>
    ConnectedDevice = 1 << 0,

    /// <summary>The user's location is being tracked and reported.</summary>
    Location = 1 << 1
}

/// <summary>
/// Service for managing foreground notification to keep the app active.
/// </summary>
public interface IForegroundService
{
    /// <summary>
    /// Starts the foreground service with a notification, declaring the service
    /// types the current keep-alive holders require. When the service is already
    /// running, implementations refresh the declared types instead of starting
    /// a second instance.
    /// </summary>
    /// <param name="requiredTypes">
    /// Foreground service capabilities that must stay declared while running.
    /// </param>
    Task StartAsync(ForegroundServiceType requiredTypes);

    /// <summary>
    /// Stops the foreground service.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Gets whether the foreground service is currently running.
    /// </summary>
    bool IsRunning { get; }
}

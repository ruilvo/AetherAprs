// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading.Tasks;

namespace AetherAprs.Services;

/// <summary>
/// Service for managing foreground notification to keep the app active.
/// </summary>
public interface IForegroundService
{
    /// <summary>
    /// Starts the foreground service with a notification.
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Stops the foreground service.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Gets whether the foreground service is currently running.
    /// </summary>
    bool IsRunning { get; }
}

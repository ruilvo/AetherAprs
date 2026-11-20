// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Reasons that can independently require the application to be kept alive
/// (foreground service notification, wake lock, battery-optimization exemption).
/// </summary>
public enum KeepAliveReason
{
    /// <summary>At least one port (RF/APRS-IS) is actively running.</summary>
    Ports,

    /// <summary>Location tracking is enabled.</summary>
    LocationTracking
}

/// <summary>
/// Reference-counted coordinator over the platform <see cref="IForegroundService"/>.
/// Multiple independent sources (ports, location tracking) can hold the app alive;
/// the underlying foreground service is started on the first acquire and stopped
/// only after the last release.
/// </summary>
public interface IKeepAliveService
{
    /// <summary>
    /// Registers an active keep-alive reason. Starts the foreground service the
    /// first time a reason becomes active.
    /// </summary>
    Task AcquireAsync(KeepAliveReason reason);

    /// <summary>
    /// Clears a keep-alive reason. Stops the foreground service once no reasons remain.
    /// </summary>
    Task ReleaseAsync(KeepAliveReason reason);
}

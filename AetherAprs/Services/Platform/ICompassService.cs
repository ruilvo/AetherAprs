// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Provides the device's compass heading (degrees clockwise from north).
/// Used to orient bearing indicators so they rotate with the phone.
/// </summary>
public interface ICompassService : IDisposable
{
    /// <summary>
    /// Gets whether a compass sensor is available on the device.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets the most recent heading in degrees (0-360), or null if unknown.
    /// </summary>
    double? HeadingDegrees { get; }

    /// <summary>
    /// Event raised when the heading updates.
    /// </summary>
    event EventHandler<double>? HeadingUpdated;

    /// <summary>
    /// Starts listening for heading updates.
    /// </summary>
    void StartListening();

    /// <summary>
    /// Stops listening for heading updates.
    /// </summary>
    void StopListening();
}

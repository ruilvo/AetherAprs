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
/// Outcome of a request to hold the application in the foreground.
/// </summary>
/// <remarks>
/// A platform can refuse the request (for example Android rejects a
/// <c>startForegroundService</c> call made from the background) or accept it and
/// never publish the notification. Implementations must report both cases as
/// <see cref="Rejected"/> rather than assuming success, because the caller turns
/// the result into the keep-alive state and must never report
/// <c>Active</c> for a service that is not actually running.
/// </remarks>
public sealed class ForegroundServiceStartResult
{
    private ForegroundServiceStartResult(bool isRunning, string? failureReason)
    {
        IsRunning = isRunning;
        FailureReason = failureReason;
    }

    /// <summary>Whether the foreground service is running with the required types.</summary>
    public bool IsRunning { get; }

    /// <summary>
    /// Why the request was refused, or <see langword="null"/> when it was
    /// honoured. This text is safe for diagnostics: it never contains user data.
    /// </summary>
    public string? FailureReason { get; }

    /// <summary>The foreground service is running and holds the required types.</summary>
    public static ForegroundServiceStartResult Started() => new(true, null);

    /// <summary>The foreground service is not running; the caller must degrade.</summary>
    public static ForegroundServiceStartResult Rejected(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(false, reason);
    }
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
    /// <returns>
    /// Whether the service is now running. A refusal is reported through the
    /// result instead of an exception so the keep-alive state machine can
    /// degrade without losing the reasons that are still held.
    /// </returns>
    Task<ForegroundServiceStartResult> StartAsync(ForegroundServiceType requiredTypes);

    /// <summary>
    /// Stops the foreground service.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Gets whether the foreground service is currently running.
    /// </summary>
    bool IsRunning { get; }
}

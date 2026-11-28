// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration;

using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Connects a port's transport to the runtime permissions it needs. Shared by the
/// startup burst and the on-demand request that runs when a port is enabled later.
/// </summary>
internal static class PortPermissions
{
    /// <summary>
    /// Gets the permissions one port needs before its session can run.
    /// </summary>
    public static IReadOnlyList<PermissionKind> GetRequiredPermissions(PortConfig port)
    {
        ArgumentNullException.ThrowIfNull(port);

        // The foreground service that keeps a running port alive needs the
        // notification permission on every supported Android level.
        List<PermissionKind> kinds = [PermissionKind.Notifications];

        if (port.TypeSettings is KissSettings
            {
                Transport: BluetoothClassicKissTransportSettings or BluetoothLeKissTransportSettings
            })
        {
            kinds.Add(PermissionKind.Bluetooth);
        }

        if (port.TypeSettings is AfskSettings)
        {
            kinds.Add(PermissionKind.Audio);
        }

        return kinds;
    }

    /// <summary>
    /// Requests the given permissions in order and records each outcome that still
    /// needs the operator.
    /// </summary>
    public static async Task RequestAsync(
        IEnumerable<PermissionKind> kinds,
        IPermissionService permissions,
        ILogger logger,
        CancellationToken cancellationToken,
        IPermissionAttentionReporter? attention = null)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(logger);

        foreach (PermissionKind kind in kinds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PermissionRequestResult result = await permissions.RequestAsync(kind, cancellationToken).ConfigureAwait(false);
            attention?.Report(result);
            LogOutcome(logger, result, Consequence(kind));
        }
    }

    private static string Consequence(PermissionKind kind) => kind switch
    {
        PermissionKind.Bluetooth => "Bluetooth ports cannot connect",
        PermissionKind.Audio => "AFSK ports cannot transmit",
        _ => "a running port may not be allowed to stay in the background"
    };

    private static void LogOutcome(ILogger logger, PermissionRequestResult result, string consequence)
    {
        switch (result.Outcome)
        {
            case PermissionOutcome.Granted:
                return;

            case PermissionOutcome.Denied:
                logger.LogWarning(
                    "{Permission} permission was denied; the operator can be asked again, but {Consequence} until then.",
                    result.Kind,
                    consequence);
                return;

            case PermissionOutcome.PermanentlyDenied:
                logger.LogWarning(
                    "{Permission} permission is permanently denied; the operator must grant it in the application settings, because {Consequence} until then.",
                    result.Kind,
                    consequence);
                return;

            default:
                logger.LogInformation(
                    "{Permission} permission is unavailable on this platform, so {Consequence} is not expected to work here.",
                    result.Kind,
                    consequence);
                return;
        }
    }
}

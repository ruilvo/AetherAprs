// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration;
using AetherAprs.Services.Platform;

using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Startup;

/// <summary>
/// Decides which runtime permissions the enabled ports need and asks for exactly
/// those, so a launch that runs no port never prompts the operator.
/// </summary>
internal static class PortStartupPermissions
{
    /// <summary>
    /// Gets whether an enabled port needs the notification permission, which the
    /// platform requires before a foreground service can run.
    /// </summary>
    public static bool RequiresNotification(IReadOnlyList<PortConfig> ports) =>
        ports.Any(port => port.IsEnabled);

    /// <summary>
    /// Gets whether an enabled port opens a Bluetooth transport.
    /// </summary>
    public static bool RequiresBluetooth(IReadOnlyList<PortConfig> ports) =>
        ports.Any(port => port.IsEnabled && IsBluetoothKiss(port));

    /// <summary>
    /// Gets whether an enabled port needs the microphone.
    /// </summary>
    public static bool RequiresAudio(IReadOnlyList<PortConfig> ports) =>
        ports.Any(port => port.IsEnabled && port.TypeSettings is AfskSettings);

    /// <summary>
    /// Requests the permissions the enabled ports need.
    /// </summary>
    /// <param name="ports">Every configured port; disabled ports are ignored.</param>
    /// <param name="permissions">The platform permission adapter.</param>
    /// <param name="logger">The logger for denied permissions.</param>
    /// <param name="cancellationToken">Cancels between prompts.</param>
    /// <param name="attention">
    /// Collects the outcomes that still need the operator, so a page can show them
    /// and offer the settings shortcut.
    /// </param>
    public static async Task RequestAsync(
        IReadOnlyList<PortConfig> ports,
        IPermissionService permissions,
        ILogger logger,
        CancellationToken cancellationToken,
        IPermissionAttentionReporter? attention = null)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(logger);

        if (!RequiresNotification(ports))
        {
            logger.LogInformation("No port is enabled, so startup requests no runtime permission.");
            return;
        }

        List<PermissionKind> kinds = [PermissionKind.Notifications];

        if (RequiresBluetooth(ports))
        {
            kinds.Add(PermissionKind.Bluetooth);
        }

        if (RequiresAudio(ports))
        {
            kinds.Add(PermissionKind.Audio);
        }

        await PortPermissions
            .RequestAsync(kinds, permissions, logger, cancellationToken, attention)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Requests the permissions one port needs when the operator enables it after
    /// the startup burst, so the prompt appears with the action that needs it.
    /// </summary>
    public static Task RequestForPortAsync(
        PortConfig port,
        IPermissionService permissions,
        ILogger logger,
        CancellationToken cancellationToken,
        IPermissionAttentionReporter? attention = null) =>
        PortPermissions.RequestAsync(
            PortPermissions.GetRequiredPermissions(port),
            permissions,
            logger,
            cancellationToken,
            attention);

    private static bool IsBluetoothKiss(PortConfig port) =>
        port.TypeSettings is KissSettings
        {
            Transport: BluetoothClassicKissTransportSettings or BluetoothLeKissTransportSettings
        };
}

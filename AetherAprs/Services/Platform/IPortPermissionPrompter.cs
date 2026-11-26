// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Requests the runtime permissions one port needs at the point the operator uses
/// the port, instead of relying on the startup burst that already happened.
/// </summary>
/// <remarks>
/// An operator who adds a Bluetooth or AFSK port after launch never saw the startup
/// prompts for those transports. Asking here keeps the request attached to the
/// action that needs the permission.
/// </remarks>
public interface IPortPermissionPrompter
{
    /// <summary>
    /// Requests the permissions the port's transport needs. A permission that is
    /// already granted or not required does not prompt, so repeated calls are safe.
    /// </summary>
    /// <param name="port">The port that is about to run.</param>
    /// <param name="cancellationToken">Cancels between prompts.</param>
    Task RequestForPortAsync(PortConfig port, CancellationToken cancellationToken = default);
}

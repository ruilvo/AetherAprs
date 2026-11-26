// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Bridges <see cref="IPortPermissionPrompter"/> to the platform permission adapter
/// and the operator-visible attention record.
/// </summary>
internal sealed class PortPermissionPrompter(
    IPermissionService permissions,
    ILogger<PortPermissionPrompter> logger,
    IPermissionAttentionReporter? attention = null) : IPortPermissionPrompter
{
    /// <inheritdoc />
    public Task RequestForPortAsync(PortConfig port, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(port);

        return PortPermissions.RequestAsync(
            PortPermissions.GetRequiredPermissions(port),
            permissions,
            logger,
            cancellationToken,
            attention);
    }
}

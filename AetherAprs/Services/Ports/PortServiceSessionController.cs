// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Adapts the existing port service to the coordinator lifecycle contract.
/// </summary>
public sealed class PortServiceSessionController(IPortService portService) : ISessionPortController
{
    public Task ConnectAsync(Guid portId, CancellationToken cancellationToken) =>
        portService.SetPortEnabledAsync(portId, true);

    public Task DisconnectAsync(Guid portId, CancellationToken cancellationToken) =>
        portService.SetPortEnabledAsync(portId, false);
}

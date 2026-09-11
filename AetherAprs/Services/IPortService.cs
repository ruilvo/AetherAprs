// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Services;

public interface IPortService
{
    IReadOnlyList<PortConfig> Ports { get; }

    event EventHandler? PortsChanged;

    event EventHandler<AprsPacket>? PacketReceived;

    Task AddPortAsync(PortConfig port);

    Task UpdatePortAsync(PortConfig port);

    Task RemovePortAsync(Guid id);

    Task SetPortEnabledAsync(Guid id, bool enabled);

    Task SendPacketAsync(Guid id, AprsPacket packet);

    Task StartAllEnabledPortsAsync();

    Task StopAllPortsAsync();
}

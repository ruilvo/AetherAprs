// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Services;

/// <summary>
/// Packet received on a specific configured port.
/// </summary>
public sealed class PortPacketReceivedEventArgs : EventArgs
{
    public required Guid PortId { get; init; }

    public required AprsPacket Packet { get; init; }
}

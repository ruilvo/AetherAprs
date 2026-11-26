// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models.Aprs.Packets;

namespace AetherAprs.Services.Ports;

public sealed class PortPacketTransmittedEventArgs(Guid portId, AprsPacket packet) : EventArgs
{
    public Guid PortId { get; } = portId;

    public AprsPacket Packet { get; } = packet;
}

public sealed class PortTransportFaultEventArgs(Guid portId, Exception exception) : EventArgs
{
    public Guid PortId { get; } = portId;

    public Exception Exception { get; } = exception;
}

public interface IPortServiceTelemetry
{
    event EventHandler<PortPacketTransmittedEventArgs>? PacketTransmitted;

    event EventHandler<PortTransportFaultEventArgs>? TransportFaulted;

    /// <summary>
    /// Number of inbound packets dropped before publication for a port, because the
    /// ingest queue was full or the packet could not be persisted.
    /// </summary>
    long GetDroppedPacketCount(Guid portId);

    /// <summary>
    /// Number of forwarding candidates received on a port that were never
    /// transmitted, because the relay queue was full, the candidate aged out, or
    /// the route was no longer available.
    /// </summary>
    long GetDroppedForwardCount(Guid portId);
}

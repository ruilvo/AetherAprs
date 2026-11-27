// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Transports.Kiss;

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

/// <summary>
/// One connection-stage report for a configured port, so a consumer can show where
/// a connect attempt stopped instead of a bare failure.
/// </summary>
public sealed class PortConnectionProgressEventArgs(Guid portId, KissConnectionProgress progress) : EventArgs
{
    public Guid PortId { get; } = portId;

    public KissConnectionProgress Progress { get; } = progress;
}

public interface IPortServiceTelemetry
{
    event EventHandler<PortPacketTransmittedEventArgs>? PacketTransmitted;

    event EventHandler<PortTransportFaultEventArgs>? TransportFaulted;

    /// <summary>
    /// Raised while a port's transport is being brought up, so a page can show the
    /// stage the attempt reached instead of a generic failure.
    /// </summary>
    event EventHandler<PortConnectionProgressEventArgs>? ConnectionProgress;

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

    /// <summary>
    /// The last connection report recorded for the port, or <see langword="null"/>
    /// when it never negotiated a KISS transport. A port that is currently stopped
    /// still reports the stage its last attempt stopped at.
    /// </summary>
    KissConnectionProgress? GetConnectionProgress(Guid portId);
}

/// <summary>
/// Telemetry that reports nothing, used when a consumer is handed a port service
/// that does not publish runtime telemetry (a configuration-only or test double).
/// </summary>
public sealed class NullPortServiceTelemetry : IPortServiceTelemetry
{
    public static NullPortServiceTelemetry Instance { get; } = new();

    private NullPortServiceTelemetry()
    {
    }

    /// <inheritdoc />
    public event EventHandler<PortPacketTransmittedEventArgs>? PacketTransmitted
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event EventHandler<PortTransportFaultEventArgs>? TransportFaulted
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event EventHandler<PortConnectionProgressEventArgs>? ConnectionProgress
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public long GetDroppedPacketCount(Guid portId) => 0;

    /// <inheritdoc />
    public long GetDroppedForwardCount(Guid portId) => 0;

    /// <inheritdoc />
    public KissConnectionProgress? GetConnectionProgress(Guid portId) => null;
}

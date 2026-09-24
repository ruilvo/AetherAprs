// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Data;

/// <summary>
/// Persisted APRS packet record for historical tracking and replay.
/// </summary>
public sealed class PacketRecord
{
    public long Id { get; set; }

    /// <summary>
    /// Source callsign (e.g., "N0CALL-5").
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Destination callsign.
    /// </summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Packet type (Position, Message, Status, Weather, Unknown).
    /// </summary>
    public string PacketType { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the packet was received.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>
    /// Timestamp embedded in the packet, if available.
    /// </summary>
    public DateTimeOffset? PacketTimestamp { get; set; }

    /// <summary>
    /// Port ID that received this packet.
    /// </summary>
    public Guid? PortId { get; set; }

    /// <summary>
    /// Raw APRS info field.
    /// </summary>
    public string RawInfo { get; set; } = string.Empty;

    // Position-specific fields (nullable for non-position packets)
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Altitude { get; set; }
    public double? Course { get; set; }
    public double? Speed { get; set; }
    public string? SymbolTable { get; set; }
    public string? SymbolCode { get; set; }
    public string? Comment { get; set; }

    // Message-specific fields (nullable for non-message packets)
    public string? MessageAddressee { get; set; }
    public string? MessageText { get; set; }
    public int? MessageNumber { get; set; }

    // Status-specific fields
    public string? StatusText { get; set; }

    // Weather-specific fields (nullable for non-weather packets)
    public double? Temperature { get; set; }
    public double? WindSpeed { get; set; }
    public double? WindDirection { get; set; }
    public double? Humidity { get; set; }
    public double? Pressure { get; set; }
    public double? RainLastHour { get; set; }
    public double? RainLast24Hours { get; set; }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using Geo;
using System;

namespace AetherAprs.Data;

/// <summary>
/// Persisted APRS packet record for historical tracking and replay.
/// Supports both received and sent packets.
/// </summary>
public sealed class PacketRecord
{
    public long Id { get; set; }

    /// <summary>
    /// Source callsign base (without SSID, e.g., "N0CALL").
    /// </summary>
    public string SourceBase { get; set; } = string.Empty;

    /// <summary>
    /// Source SSID (0-15).
    /// </summary>
    public AprsSsid SourceSsid { get; set; }

    /// <summary>
    /// Destination callsign base (without SSID).
    /// </summary>
    public string DestinationBase { get; set; } = string.Empty;

    /// <summary>
    /// Destination SSID (0-15).
    /// </summary>
    public AprsSsid DestinationSsid { get; set; }

    /// <summary>
    /// Packet type discriminator (Position, Message, Status, Weather, Unknown).
    /// </summary>
    public string PacketType { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the packet was received or sent (UTC).
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Timestamp embedded in the packet, if available (UTC).
    /// </summary>
    public DateTime? PacketTimestamp { get; set; }

    /// <summary>
    /// Port ID that received or sent this packet.
    /// </summary>
    public Guid? PortId { get; set; }

    /// <summary>
    /// True if this packet was sent by us, false if received.
    /// </summary>
    public bool IsOutbound { get; set; }

    /// <summary>
    /// Raw APRS info field.
    /// </summary>
    public string RawInfo { get; set; } = string.Empty;

    // Owned types for packet-type-specific data
    public PositionDataRecord? Position { get; set; }
    public MessageDataRecord? Message { get; set; }
    public StatusDataRecord? Status { get; set; }
    public WeatherDataRecord? Weather { get; set; }
}

/// <summary>
/// Position packet data.
/// </summary>
public sealed class PositionDataRecord
{
    /// <summary>
    /// Geographic coordinate (latitude, longitude) using the Geo library.
    /// </summary>
    public Coordinate Location { get; set; } = new Coordinate();
    
    public double? Altitude { get; set; }
    public double? Course { get; set; }
    public double? Speed { get; set; }
    public SymbolTable SymbolTable { get; set; }
    public SymbolCode SymbolCode { get; set; }
    public string? Comment { get; set; }
}

/// <summary>
/// Message packet data.
/// </summary>
public sealed class MessageDataRecord
{
    /// <summary>
    /// Message addressee callsign base (without SSID).
    /// </summary>
    public string AddresseeBase { get; set; } = string.Empty;

    /// <summary>
    /// Message addressee SSID.
    /// </summary>
    public AprsSsid AddresseeSsid { get; set; }

    /// <summary>
    /// Message text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Message number for acknowledgment tracking.
    /// </summary>
    public int? Number { get; set; }

    /// <summary>
    /// Delivery status (for outbound messages).
    /// </summary>
    public MessageDeliveryStatus? DeliveryStatus { get; set; }

    /// <summary>
    /// Number of transmission retries (for outbound messages).
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Next scheduled retry time (for outbound messages).
    /// </summary>
    public DateTime? NextRetryTime { get; set; }
}

/// <summary>
/// Status packet data.
/// </summary>
public sealed class StatusDataRecord
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Weather packet data.
/// </summary>
public sealed class WeatherDataRecord
{
    public double? Temperature { get; set; }
    public double? WindSpeed { get; set; }
    public double? WindDirection { get; set; }
    public double? Humidity { get; set; }
    public double? Pressure { get; set; }
    public double? RainLastHour { get; set; }
    public double? RainLast24Hours { get; set; }
}

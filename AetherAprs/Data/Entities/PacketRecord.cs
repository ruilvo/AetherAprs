// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using Geo;
using System;

namespace AetherAprs.Data.Entities;

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

    /// <summary>
    /// Raw AX.25 frame as uppercase hexadecimal (RF receive path only).
    /// Empty for packets that did not originate from a KISS frame (e.g. APRS-IS).
    /// </summary>
    public string RawFrameHex { get; set; } = string.Empty;

    /// <summary>
    /// Raw path portion of the packet (<c>source&gt;dest,path</c>).
    /// </summary>
    public string RawPath { get; set; } = string.Empty;

    // Owned types for packet-type-specific data
    public PositionDataRecord? Position { get; set; }
    public MessageDataRecord? Message { get; set; }
    public StatusDataRecord? Status { get; set; }
    public WeatherDataRecord? Weather { get; set; }
    public TelemetryDataRecord? Telemetry { get; set; }
    public CapabilitiesDataRecord? Capabilities { get; set; }

#pragma warning disable CA1720 // Identifier 'Object' mirrors the APRS object packet type
    public ObjectDataRecord? Object { get; set; }
#pragma warning restore CA1720
    public ItemDataRecord? Item { get; set; }
    public BulletinDataRecord? Bulletin { get; set; }
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

/// <summary>
/// Telemetry packet data (T#nnn format). Analog channels are stored as five
/// fixed nullable columns (APRS telemetry supports up to five analog values).
/// </summary>
public sealed class TelemetryDataRecord
{
    public int SequenceNumber { get; set; }
    public double? Analog1 { get; set; }
    public double? Analog2 { get; set; }
    public double? Analog3 { get; set; }
    public double? Analog4 { get; set; }
    public double? Analog5 { get; set; }
    public byte? DigitalValue { get; set; }
}

/// <summary>
/// Station capabilities packet data (&lt; format).
/// </summary>
public sealed class CapabilitiesDataRecord
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Object packet data (named position report, live or killed).
/// </summary>
public sealed class ObjectDataRecord
{
    /// <summary>
    /// Object name (up to 9 characters).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether the object is live (true) or killed (false).
    /// </summary>
    public bool IsLive { get; set; }

    /// <summary>
    /// Raw 7-character <c>DDHHMMz</c> timestamp from the object report, if present.
    /// </summary>
    public string? RawTimestamp { get; set; }

    public Coordinate Location { get; set; } = new Coordinate();
    public double? Altitude { get; set; }
    public double? Course { get; set; }
    public double? Speed { get; set; }
    public SymbolTable SymbolTable { get; set; }
    public SymbolCode SymbolCode { get; set; }
    public string? Comment { get; set; }
}

/// <summary>
/// Item packet data (named position report, live or killed).
/// </summary>
public sealed class ItemDataRecord
{
    /// <summary>
    /// Item name (3-9 characters).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether the item is live (true) or killed (false).
    /// </summary>
    public bool IsLive { get; set; }

    public Coordinate Location { get; set; } = new Coordinate();
    public double? Altitude { get; set; }
    public double? Course { get; set; }
    public double? Speed { get; set; }
    public SymbolTable SymbolTable { get; set; }
    public SymbolCode SymbolCode { get; set; }
    public string? Comment { get; set; }
}

/// <summary>
/// Bulletin packet data (broadcast message addressed to a BLN-prefixed name).
/// </summary>
public sealed class BulletinDataRecord
{
    /// <summary>
    /// Bulletin name (the raw addressee, e.g. <c>BLN0SPCL</c>).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Bulletin text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Optional message number from the <c>{nn}</c> suffix.
    /// </summary>
    public int? Number { get; set; }
}

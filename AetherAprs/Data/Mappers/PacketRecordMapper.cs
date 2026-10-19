// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data.Entities;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Models.Messaging;
using Geo;
using Microsoft.Extensions.Logging;
using System;

namespace AetherAprs.Data.Mappers;

/// <summary>
/// String discriminators stored in the Packets.PacketType column (M5). Centralizing
/// them prevents drift between the mapper and query code that filters on the column.
/// </summary>
internal static class PacketTypes
{
    public const string Position = "Position";
    public const string Message = "Message";
    public const string MessageAck = "MessageAck";
    public const string MessageRej = "MessageRej";
    public const string Status = "Status";
    public const string Weather = "Weather";
    public const string Telemetry = "Telemetry";
    public const string Capabilities = "Capabilities";
    public const string Unknown = "Unknown";
}

internal static class PacketRecordMapper
{
    /// <summary>
    /// Resolves the PacketType discriminator via an explicit type switch instead
    /// of reflecting on class names, so renaming a packet class cannot silently
    /// corrupt stored values (M5).
    /// </summary>
    private static string ResolvePacketType(AprsPacket packet) => packet switch
    {
        PositionPacket => PacketTypes.Position,
        MessagePacket => PacketTypes.Message,
        MessageAckPacket => PacketTypes.MessageAck,
        MessageRejPacket => PacketTypes.MessageRej,
        StatusPacket => PacketTypes.Status,
        WeatherPacket => PacketTypes.Weather,
        TelemetryPacket => PacketTypes.Telemetry,
        CapabilitiesPacket => PacketTypes.Capabilities,
        _ => PacketTypes.Unknown
    };

    /// <summary>
    /// Converts an APRS packet to a database record for storage.
    /// </summary>
    /// <param name="packet">The APRS packet to convert.</param>
    /// <param name="portId">The port that received or will send the packet.</param>
    /// <param name="timestamp">The timestamp when the packet was received or sent.</param>
    /// <param name="isOutbound">True if this is a sent packet, false if received.</param>
    public static PacketRecord ToRecord(
        AprsPacket packet,
        Guid? portId,
        DateTimeOffset timestamp,
        bool isOutbound)
    {
        var record = new PacketRecord
        {
            SourceBase = packet.Source.Base,
            SourceSsid = packet.Source.Ssid.HasValue 
                ? (AprsSsid)packet.Source.Ssid.Value 
                : AprsSsid.PrimaryStation,
            DestinationBase = packet.Destination.Base,
            DestinationSsid = packet.Destination.Ssid.HasValue 
                ? (AprsSsid)packet.Destination.Ssid.Value 
                : AprsSsid.PrimaryStation,
            PacketType = ResolvePacketType(packet),
            Timestamp = timestamp.UtcDateTime,
            PacketTimestamp = packet.Timestamp?.UtcDateTime,
            PortId = portId,
            IsOutbound = isOutbound,
            RawInfo = packet.Raw
        };

        // Map type-specific fields to owned types
        switch (packet)
        {
            case PositionPacket pos:
                record.Position = new PositionDataRecord
                {
                    Location = pos.Location,
                    Altitude = pos.Altitude,
                    Course = pos.Course,
                    Speed = pos.Speed,
                    SymbolTable = pos.Symbol.Table,
                    SymbolCode = pos.Symbol.Code,
                    Comment = pos.Comment
                };
                break;

            case MessagePacket msg:
                record.Message = new MessageDataRecord
                {
                    AddresseeBase = msg.Addressee.Base,
                    AddresseeSsid = msg.Addressee.Ssid.HasValue 
                        ? (AprsSsid)msg.Addressee.Ssid.Value 
                        : AprsSsid.PrimaryStation,
                    Text = msg.Text,
                    Number = msg.MessageNumber,
                    // Delivery tracking fields are null for received messages
                    DeliveryStatus = isOutbound ? MessageDeliveryStatus.Pending : null,
                    RetryCount = 0,
                    NextRetryTime = null
                };
                break;

            case StatusPacket status:
                record.Status = new StatusDataRecord
                {
                    Text = status.Text
                };
                break;

            case WeatherPacket weather:
                record.Weather = new WeatherDataRecord
                {
                    Temperature = weather.Temperature,
                    WindSpeed = weather.WindSpeed,
                    WindDirection = weather.WindDirection,
                    Humidity = weather.Humidity,
                    Pressure = weather.Pressure,
                    RainLastHour = weather.Rain1h,
                    RainLast24Hours = weather.Rain24h
                };
                break;
        }

        return record;
    }

    /// <summary>
    /// Converts a database record back to an APRS packet.
    /// </summary>
    /// <param name="record">The database record to convert.</param>
    /// <param name="logger">Optional logger for diagnostic output on parse failures.</param>
    public static AprsPacket? MapToPacket(PacketRecord record, ILogger? logger = null)
    {
        try
        {
            var source = new Callsign(record.SourceBase, (int)record.SourceSsid);
            var destination = new Callsign(record.DestinationBase, (int)record.DestinationSsid);

            DateTimeOffset? timestamp = record.PacketTimestamp.HasValue
                ? new DateTimeOffset(record.PacketTimestamp.Value, TimeSpan.Zero)
                : null;

            AprsPacket packet = record.PacketType switch
            {
                PacketTypes.Position when record.Position != null =>
                    new PositionPacket
                    {
                        Source = source,
                        Destination = destination,
                        Location = record.Position.Location,
                        Altitude = record.Position.Altitude,
                        Course = record.Position.Course,
                        Speed = record.Position.Speed,
                        Symbol = new Symbol(
                            record.Position.SymbolTable,
                            record.Position.SymbolCode
                        ),
                        Comment = record.Position.Comment,
                        Raw = record.RawInfo,
                        Timestamp = timestamp
                    },

                PacketTypes.Message when record.Message != null =>
                    new MessagePacket
                    {
                        Source = source,
                        Destination = destination,
                        Addressee = new Callsign(
                            record.Message.AddresseeBase,
                            (int)record.Message.AddresseeSsid
                        ),
                        Text = record.Message.Text,
                        MessageNumber = record.Message.Number,
                        Raw = record.RawInfo,
                        Timestamp = timestamp
                    },

                PacketTypes.Status when record.Status != null =>
                    new StatusPacket
                    {
                        Source = source,
                        Destination = destination,
                        Text = record.Status.Text,
                        Raw = record.RawInfo,
                        Timestamp = timestamp
                    },

                PacketTypes.Weather when record.Weather != null =>
                    new WeatherPacket
                    {
                        Source = source,
                        Destination = destination,
                        Temperature = record.Weather.Temperature,
                        WindSpeed = record.Weather.WindSpeed,
                        WindDirection = record.Weather.WindDirection,
                        Humidity = record.Weather.Humidity,
                        Pressure = record.Weather.Pressure,
                        Rain1h = record.Weather.RainLastHour,
                        Rain24h = record.Weather.RainLast24Hours,
                        Raw = record.RawInfo,
                        Timestamp = timestamp
                    },

                _ =>
                    new UnknownPacket
                    {
                        Source = source,
                        Destination = destination,
                        Raw = record.RawInfo,
                        Timestamp = timestamp
                    }
            };

            return packet;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                ex,
                "Failed to convert PacketRecord to AprsPacket: source {Source}, type {Type}, rawInfo {RawInfo}",
                $"{record.SourceBase}-{record.SourceSsid}",
                record.PacketType,
                record.RawInfo
            );
            return null;
        }
    }
}

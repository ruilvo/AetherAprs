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
using System.Collections.Generic;

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
    public const string Object = "Object";
    public const string Item = "Item";
    public const string Bulletin = "Bulletin";
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
        ObjectPacket => PacketTypes.Object,
        ItemPacket => PacketTypes.Item,
        BulletinPacket => PacketTypes.Bulletin,
        _ => PacketTypes.Unknown
    };

    /// <summary>
    /// Converts an APRS packet to a database record for storage.
    /// </summary>
    /// <param name="packet">The APRS packet to convert.</param>
    /// <param name="portId">The port that received or will send the packet.</param>
    /// <param name="timestamp">The timestamp when the packet was received or sent.</param>
    /// <param name="isOutbound">True if this is a sent packet, false if received.</param>
    /// <param name="deliveryStatus">
    /// Delivery status for an outbound message packet. Defaults to
    /// <see cref="MessageDeliveryStatus.Pending"/> when omitted so retry
    /// bookkeeping can supply the real status instead of the mapper guessing it.
    /// </param>
    public static PacketRecord ToRecord(
        AprsPacket packet,
        Guid? portId,
        DateTimeOffset timestamp,
        bool isOutbound,
        MessageDeliveryStatus? deliveryStatus = null)
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
            RawInfo = packet.Raw,
            RawFrameHex = packet.RawFrameHex,
            RawPath = packet.RawPath
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
                    // Delivery tracking fields are null for received messages.
                    DeliveryStatus = isOutbound
                        ? deliveryStatus ?? MessageDeliveryStatus.Pending
                        : null,
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

            case TelemetryPacket telemetry:
                record.Telemetry = new TelemetryDataRecord
                {
                    SequenceNumber = telemetry.SequenceNumber,
                    Analog1 = telemetry.AnalogValues.Count > 0 ? telemetry.AnalogValues[0] : null,
                    Analog2 = telemetry.AnalogValues.Count > 1 ? telemetry.AnalogValues[1] : null,
                    Analog3 = telemetry.AnalogValues.Count > 2 ? telemetry.AnalogValues[2] : null,
                    Analog4 = telemetry.AnalogValues.Count > 3 ? telemetry.AnalogValues[3] : null,
                    Analog5 = telemetry.AnalogValues.Count > 4 ? telemetry.AnalogValues[4] : null,
                    DigitalValue = telemetry.DigitalValue
                };
                break;

            case CapabilitiesPacket capabilities:
                record.Capabilities = new CapabilitiesDataRecord
                {
                    Text = capabilities.Text
                };
                break;

            case ObjectPacket obj:
                record.Object = new ObjectDataRecord
                {
                    Name = obj.Name,
                    IsLive = obj.IsLive,
                    RawTimestamp = obj.RawTimestamp,
                    Location = obj.Location,
                    Altitude = obj.Altitude,
                    Course = obj.Course,
                    Speed = obj.Speed,
                    SymbolTable = obj.Symbol.Table,
                    SymbolCode = obj.Symbol.Code,
                    Comment = obj.Comment
                };
                break;

            case ItemPacket item:
                record.Item = new ItemDataRecord
                {
                    Name = item.Name,
                    IsLive = item.IsLive,
                    Location = item.Location,
                    Altitude = item.Altitude,
                    Course = item.Course,
                    Speed = item.Speed,
                    SymbolTable = item.Symbol.Table,
                    SymbolCode = item.Symbol.Code,
                    Comment = item.Comment
                };
                break;

            case BulletinPacket bulletin:
                record.Bulletin = new BulletinDataRecord
                {
                    Name = bulletin.Name,
                    Text = bulletin.Text,
                    Number = bulletin.MessageNumber
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
                        Addressee = new AprsAddress(
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

                PacketTypes.Telemetry when record.Telemetry != null =>
                    new TelemetryPacket
                    {
                        Source = source,
                        Destination = destination,
                        SequenceNumber = record.Telemetry.SequenceNumber,
                        AnalogValues = BuildAnalogValues(record.Telemetry),
                        DigitalValue = record.Telemetry.DigitalValue,
                        Raw = record.RawInfo,
                        Timestamp = timestamp
                    },

                PacketTypes.Capabilities when record.Capabilities != null =>
                    new CapabilitiesPacket
                    {
                        Source = source,
                        Destination = destination,
                        Text = record.Capabilities.Text,
                        Raw = record.RawInfo,
                        Timestamp = timestamp
                    },

                PacketTypes.Object when record.Object != null =>
                    new ObjectPacket
                    {
                        Source = source,
                        Destination = destination,
                        Name = record.Object.Name,
                        IsLive = record.Object.IsLive,
                        RawTimestamp = record.Object.RawTimestamp,
                        Location = record.Object.Location,
                        Altitude = record.Object.Altitude,
                        Course = record.Object.Course,
                        Speed = record.Object.Speed,
                        Symbol = new Symbol(
                            record.Object.SymbolTable,
                            record.Object.SymbolCode
                        ),
                        Comment = record.Object.Comment,
                        Raw = record.RawInfo,
                        Timestamp = timestamp
                    },

                PacketTypes.Item when record.Item != null =>
                    new ItemPacket
                    {
                        Source = source,
                        Destination = destination,
                        Name = record.Item.Name,
                        IsLive = record.Item.IsLive,
                        Location = record.Item.Location,
                        Altitude = record.Item.Altitude,
                        Course = record.Item.Course,
                        Speed = record.Item.Speed,
                        Symbol = new Symbol(
                            record.Item.SymbolTable,
                            record.Item.SymbolCode
                        ),
                        Comment = record.Item.Comment,
                        Raw = record.RawInfo,
                        Timestamp = timestamp
                    },

                PacketTypes.Bulletin when record.Bulletin != null =>
                    new BulletinPacket
                    {
                        Source = source,
                        Destination = destination,
                        Name = record.Bulletin.Name,
                        Text = record.Bulletin.Text,
                        MessageNumber = record.Bulletin.Number,
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

            // Restore the raw frame/path metadata (present on RF/IS receive paths)
            // via a with-expression so the concrete packet type is preserved.
            return packet with
            {
                RawFrameHex = record.RawFrameHex,
                RawPath = record.RawPath
            };
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

    /// <summary>
    /// Rebuilds the telemetry analog-channel list from the five fixed nullable
    /// columns, stopping at the first null (channels are populated contiguously).
    /// </summary>
    private static IReadOnlyList<double> BuildAnalogValues(TelemetryDataRecord telemetry)
    {
        var values = new List<double>(5);
        foreach (var channel in new double?[]
                 {
                     telemetry.Analog1,
                     telemetry.Analog2,
                     telemetry.Analog3,
                     telemetry.Analog4,
                     telemetry.Analog5
                 })
        {
            if (channel is null)
            {
                break;
            }

            values.Add(channel.Value);
        }

        return values;
    }
}

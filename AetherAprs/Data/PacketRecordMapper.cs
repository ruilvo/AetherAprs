// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using System;

namespace AetherAprs.Data;

internal static class PacketRecordMapper
{
    public static PacketRecord ToRecord(AprsPacket packet, Guid? portId, DateTimeOffset receivedAt)
    {
        var record = new PacketRecord
        {
            Source = packet.Source.ToString(),
            Destination = packet.Destination.ToString(),
            PacketType = packet.GetType().Name.Replace("Packet", ""),
            ReceivedAt = receivedAt.UtcDateTime,
            PacketTimestamp = packet.Timestamp?.UtcDateTime,
            PortId = portId,
            RawInfo = packet.Raw
        };

        // Map type-specific fields
        switch (packet)
        {
            case PositionPacket pos:
                record.Latitude = pos.Latitude;
                record.Longitude = pos.Longitude;
                record.Altitude = pos.Altitude;
                record.Course = pos.Course;
                record.Speed = pos.Speed;
                record.SymbolTable = pos.Symbol.TableChar.ToString();
                record.SymbolCode = pos.Symbol.CodeChar.ToString();
                record.Comment = pos.Comment;
                break;

            case MessagePacket msg:
                record.MessageAddressee = msg.Addressee.ToString();
                record.MessageText = msg.Text;
                record.MessageNumber = msg.MessageNumber;
                break;

            case StatusPacket status:
                record.StatusText = status.Text;
                break;

            case WeatherPacket weather:
                record.Temperature = weather.Temperature;
                record.WindSpeed = weather.WindSpeed;
                record.WindDirection = weather.WindDirection;
                record.Humidity = weather.Humidity;
                record.Pressure = weather.Pressure;
                record.RainLastHour = weather.Rain1h;
                record.RainLast24Hours = weather.Rain24h;
                break;
        }

        return record;
    }

    public static AprsPacket? MapToPacket(PacketRecord record)
    {
        try
        {
            if (!Callsign.TryParse(record.Source, out var source))
            {
                return null;
            }
            
            if (!Callsign.TryParse(record.Destination, out var destination))
            {
                return null;
            }

            AprsPacket packet = record.PacketType switch
            {
                "Position" when record.Latitude.HasValue && record.Longitude.HasValue =>
                    new PositionPacket
                    {
                        Source = source,
                        Destination = destination,
                        Latitude = record.Latitude.Value,
                        Longitude = record.Longitude.Value,
                        Altitude = record.Altitude,
                        Course = record.Course,
                        Speed = record.Speed,
                        Symbol = new Symbol(
                            (record.SymbolTable ?? "/")[0].ToSymbolTable(),
                            (record.SymbolCode ?? "[")[0].ToSymbolCode()
                        ),
                        Comment = record.Comment,
                        Raw = record.RawInfo ?? "",
                        Timestamp = record.PacketTimestamp.HasValue 
                            ? new DateTimeOffset(record.PacketTimestamp.Value, TimeSpan.Zero) 
                            : null
                    },

                "Message" when !string.IsNullOrEmpty(record.MessageAddressee) && 
                               Callsign.TryParse(record.MessageAddressee, out var addressee) =>
                    new MessagePacket
                    {
                        Source = source,
                        Destination = destination,
                        Addressee = addressee,
                        Text = record.MessageText ?? "",
                        MessageNumber = record.MessageNumber,
                        Raw = record.RawInfo ?? "",
                        Timestamp = record.PacketTimestamp.HasValue 
                            ? new DateTimeOffset(record.PacketTimestamp.Value, TimeSpan.Zero) 
                            : null
                    },

                "Status" =>
                    new StatusPacket
                    {
                        Source = source,
                        Destination = destination,
                        Text = record.StatusText ?? "",
                        Raw = record.RawInfo ?? "",
                        Timestamp = record.PacketTimestamp.HasValue 
                            ? new DateTimeOffset(record.PacketTimestamp.Value, TimeSpan.Zero) 
                            : null
                    },

                "Weather" =>
                    new WeatherPacket
                    {
                        Source = source,
                        Destination = destination,
                        Temperature = record.Temperature,
                        WindSpeed = record.WindSpeed,
                        WindDirection = record.WindDirection,
                        Humidity = record.Humidity,
                        Pressure = record.Pressure,
                        Rain1h = record.RainLastHour,
                        Rain24h = record.RainLast24Hours,
                        Raw = record.RawInfo ?? "",
                        Timestamp = record.PacketTimestamp.HasValue 
                            ? new DateTimeOffset(record.PacketTimestamp.Value, TimeSpan.Zero) 
                            : null
                    },

                _ =>
                    new UnknownPacket
                    {
                        Source = source,
                        Destination = destination,
                        Raw = record.RawInfo ?? "",
                        Timestamp = record.PacketTimestamp.HasValue 
                            ? new DateTimeOffset(record.PacketTimestamp.Value, TimeSpan.Zero) 
                            : null
                    }
            };

            return packet;
        }
        catch
        {
            // If we can't parse the record, return null
            return null;
        }
    }
}

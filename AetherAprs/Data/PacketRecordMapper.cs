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
            ReceivedAt = receivedAt,
            PacketTimestamp = packet.Timestamp,
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
}

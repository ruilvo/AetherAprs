// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Protocols.Aprs;

using AetherAprs.Models;
using NetTopologySuite.Geometries;
using System;
using System.Globalization;

public static class AprsPayloadCodec
{
    public static string Encode(AprsEntry entry)
    {
        entry.Validate();

        return entry.Kind switch
        {
            AprsEntryKind.Station => EncodeStation(entry),
            AprsEntryKind.Object => EncodeObject(entry),
            AprsEntryKind.Item => EncodeItem(entry),
            AprsEntryKind.Message => EncodeMessage(entry),
            _ => throw new InvalidOperationException($"Unsupported APRS entry kind '{entry.Kind}'."),
        };
    }

    public static AprsEntry Decode(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            throw new InvalidOperationException("APRS payload is required.");
        }

        return payload[0] switch
        {
            '!' => DecodeStation(payload, AprsStationDataType.PositionWithoutMessaging),
            '=' => DecodeStation(payload, AprsStationDataType.PositionWithMessaging),
            '/' => DecodeStation(payload, AprsStationDataType.TimestampedPositionWithoutMessaging),
            '@' => DecodeStation(payload, AprsStationDataType.TimestampedPositionWithMessaging),
            ';' => DecodeObject(payload),
            ')' => DecodeItem(payload),
            ':' => DecodeMessage(payload),
            _ => throw new InvalidOperationException($"Unsupported APRS payload type '{payload[0]}'."),
        };
    }

    private static string EncodeStation(AprsEntry entry)
    {
        var prefix = entry.StationDataType switch
        {
            AprsStationDataType.PositionWithoutMessaging => '!',
            AprsStationDataType.PositionWithMessaging => '=',
            AprsStationDataType.TimestampedPositionWithoutMessaging => '/',
            AprsStationDataType.TimestampedPositionWithMessaging => '@',
            _ => throw new InvalidOperationException("Station entries require a station data type."),
        };

        var timestamp = prefix is '/' or '@' ? FormatTimestamp(entry.AprsTimestamp, entry.Timestamp) : string.Empty;
        var (symbolTable, symbolCode) = EncodeSymbol(entry.Symbol!);
        var coordinates = FormatCoordinates(entry.Location!, symbolTable, symbolCode);
        return $"{prefix}{timestamp}{coordinates}{entry.Comment ?? string.Empty}";
    }

    private static string EncodeObject(AprsEntry entry)
    {
        var (symbolTable, symbolCode) = EncodeSymbol(entry.Symbol!);
        var state = entry.IsAlive ? '*' : '_';
        var coordinates = FormatCoordinates(entry.Location!, symbolTable, symbolCode);
        return $";{entry.ObjectName!.PadRight(9)}{state}{FormatTimestamp(entry.AprsTimestamp, entry.Timestamp)}{coordinates}{entry.Comment ?? string.Empty}";
    }

    private static string EncodeItem(AprsEntry entry)
    {
        var (symbolTable, symbolCode) = EncodeSymbol(entry.Symbol!);
        var state = entry.IsAlive ? '!' : '_';
        var coordinates = FormatCoordinates(entry.Location!, symbolTable, symbolCode);
        return $"){entry.ItemName!}{state}{coordinates}{entry.Comment ?? string.Empty}";
    }

    private static string EncodeMessage(AprsEntry entry)
    {
        var recipient = entry.MessageRecipient!.PadRight(9);
        var id = entry.MessageId is null ? string.Empty : $"{{{entry.MessageId}";
        return $":{recipient}:{entry.MessageText!}{id}";
    }

    private static AprsEntry DecodeStation(string payload, AprsStationDataType stationDataType)
    {
        var offset = 1;
        AprsPartialTimestamp? aprsTimestamp = null;

        if (stationDataType is AprsStationDataType.TimestampedPositionWithoutMessaging or AprsStationDataType.TimestampedPositionWithMessaging)
        {
            aprsTimestamp = ParseTimestamp(payload.Substring(offset, 7));
            offset += 7;
        }

        var (location, symbol, consumed) = ParseCoordinates(payload, offset);
        var comment = payload[(offset + consumed)..];

        return new AprsEntry
        {
            Kind = AprsEntryKind.Station,
            Location = location,
            Symbol = symbol,
            AprsTimestamp = aprsTimestamp,
            StationDataType = stationDataType,
            Comment = string.IsNullOrEmpty(comment) ? null : comment,
        };
    }

    private static AprsEntry DecodeObject(string payload)
    {
        if (payload.Length < 37)
        {
            throw new InvalidOperationException("Object payload is too short.");
        }

        var name = payload.Substring(1, 9).TrimEnd();
        var state = payload[10];
        if (state is not '*' and not '_')
        {
            throw new InvalidOperationException("Object payload contains an invalid object state.");
        }

        var aprsTimestamp = ParseTimestamp(payload.Substring(11, 7));
        var (location, symbol, consumed) = ParseCoordinates(payload, 18);
        var comment = payload[(18 + consumed)..];

        return new AprsEntry
        {
            Kind = AprsEntryKind.Object,
            ObjectName = name,
            IsAlive = state == '*',
            AprsTimestamp = aprsTimestamp,
            Location = location,
            Symbol = symbol,
            Comment = string.IsNullOrEmpty(comment) ? null : comment,
        };
    }

    private static AprsEntry DecodeItem(string payload)
    {
        var markerIndex = payload.IndexOf('!', 1);
        var deadIndex = payload.IndexOf('_', 1);
        var separatorIndex = markerIndex >= 0 && deadIndex >= 0 ? Math.Min(markerIndex, deadIndex) : Math.Max(markerIndex, deadIndex);

        if (separatorIndex is < 2 or > 10)
        {
            throw new InvalidOperationException("Item payload is missing a valid name separator.");
        }

        var name = payload.Substring(1, separatorIndex - 1);
        if (name.Contains('!') || name.Contains('_'))
        {
            throw new InvalidOperationException("Item payload contains an invalid item name.");
        }

        var isAlive = payload[separatorIndex] == '!';
        var (location, symbol, consumed) = ParseCoordinates(payload, separatorIndex + 1);
        var comment = payload[(separatorIndex + 1 + consumed)..];

        return new AprsEntry
        {
            Kind = AprsEntryKind.Item,
            ItemName = name,
            IsAlive = isAlive,
            Location = location,
            Symbol = symbol,
            Comment = string.IsNullOrEmpty(comment) ? null : comment,
        };
    }

    private static AprsEntry DecodeMessage(string payload)
    {
        if (payload.Length < 11 || payload[10] != ':')
        {
            throw new InvalidOperationException("Message payload is malformed.");
        }

        var recipient = payload.Substring(1, 9).TrimEnd();
        var body = payload[11..];
        var messageIdIndex = body.LastIndexOf('{');
        var messageText = messageIdIndex >= 0 ? body[..messageIdIndex] : body;
        var messageId = messageIdIndex >= 0 ? body[(messageIdIndex + 1)..] : null;

        return new AprsEntry
        {
            Kind = AprsEntryKind.Message,
            MessageRecipient = recipient,
            MessageText = messageText,
            MessageId = string.IsNullOrEmpty(messageId) ? null : messageId,
        };
    }

    private static string FormatCoordinates(Point location, char symbolTable, char symbolCode)
    {
        var latitude = FormatLatitude(location.Y);
        var longitude = FormatLongitude(location.X);
        return $"{latitude}{symbolTable}{longitude}{symbolCode}";
    }

    private static (Point Location, AprsSymbol Symbol, int ConsumedLength) ParseCoordinates(string payload, int offset)
    {
        if (payload.Length < offset + 19)
        {
            throw new InvalidOperationException("Coordinate payload is too short.");
        }

        var latitudeText = payload.Substring(offset, 8);
        var symbolTableSelector = payload[offset + 8];
        var longitudeText = payload.Substring(offset + 9, 9);
        var symbolCode = payload[offset + 18];

        var latitude = ParseLatitude(latitudeText);
        var longitude = ParseLongitude(longitudeText);
        var symbol = DecodeSymbol(symbolTableSelector, symbolCode);

        return (new Point(longitude, latitude), symbol, 19);
    }

    private static string FormatLatitude(double latitude)
    {
        if (latitude is < -90 or > 90)
        {
            throw new InvalidOperationException("Latitude must be between -90 and 90 degrees.");
        }

        var hemisphere = latitude >= 0 ? 'N' : 'S';
        var absoluteLatitude = Math.Abs(latitude);
        var degrees = (int)Math.Floor(absoluteLatitude);
        var minutes = (absoluteLatitude - degrees) * 60d;
        return string.Format(CultureInfo.InvariantCulture, "{0:00}{1:00.00}{2}", degrees, minutes, hemisphere);
    }

    private static string FormatLongitude(double longitude)
    {
        if (longitude is < -180 or > 180)
        {
            throw new InvalidOperationException("Longitude must be between -180 and 180 degrees.");
        }

        var hemisphere = longitude >= 0 ? 'E' : 'W';
        var absoluteLongitude = Math.Abs(longitude);
        var degrees = (int)Math.Floor(absoluteLongitude);
        var minutes = (absoluteLongitude - degrees) * 60d;
        return string.Format(CultureInfo.InvariantCulture, "{0:000}{1:00.00}{2}", degrees, minutes, hemisphere);
    }

    private static double ParseLatitude(string latitude)
    {
        if (latitude.Length != 8 || latitude[7] is not ('N' or 'S'))
        {
            throw new InvalidOperationException("Latitude is malformed.");
        }

        var degrees = double.Parse(latitude[..2], CultureInfo.InvariantCulture);
        var minutes = double.Parse(latitude[2..7], CultureInfo.InvariantCulture);
        if (degrees > 90 || minutes >= 60)
        {
            throw new InvalidOperationException("Latitude is outside APRS limits.");
        }

        var value = degrees + (minutes / 60d);
        return latitude[7] == 'S' ? -value : value;
    }

    private static double ParseLongitude(string longitude)
    {
        if (longitude.Length != 9 || longitude[8] is not ('E' or 'W'))
        {
            throw new InvalidOperationException("Longitude is malformed.");
        }

        var degrees = double.Parse(longitude[..3], CultureInfo.InvariantCulture);
        var minutes = double.Parse(longitude[3..8], CultureInfo.InvariantCulture);
        if (degrees > 180 || minutes >= 60)
        {
            throw new InvalidOperationException("Longitude is outside APRS limits.");
        }

        var value = degrees + (minutes / 60d);
        return longitude[8] == 'W' ? -value : value;
    }

    private static string FormatTimestamp(AprsPartialTimestamp? aprsTimestamp, DateTimeOffset? timestamp)
    {
        var normalized = aprsTimestamp ?? (timestamp is not null
            ? new AprsPartialTimestamp
            {
                Day = timestamp.Value.ToUniversalTime().Day,
                Hour = timestamp.Value.ToUniversalTime().Hour,
                Minute = timestamp.Value.ToUniversalTime().Minute,
                Suffix = 'z',
            }
            : throw new InvalidOperationException("An APRS timestamp is required."));

        normalized.Validate();
        return string.Format(CultureInfo.InvariantCulture, "{0:00}{1:00}{2:00}{3}", normalized.Day, normalized.Hour, normalized.Minute, normalized.Suffix);
    }

    private static AprsPartialTimestamp ParseTimestamp(string timestamp)
    {
        if (timestamp.Length != 7)
        {
            throw new InvalidOperationException("APRS timestamp must be 7 characters long.");
        }

        var parsed = new AprsPartialTimestamp
        {
            Day = int.Parse(timestamp[..2], CultureInfo.InvariantCulture),
            Hour = int.Parse(timestamp.Substring(2, 2), CultureInfo.InvariantCulture),
            Minute = int.Parse(timestamp.Substring(4, 2), CultureInfo.InvariantCulture),
            Suffix = timestamp[6],
        };

        parsed.Validate();
        return parsed;
    }

    private static (char TableSelector, char SymbolCode) EncodeSymbol(AprsSymbol symbol)
    {
        var tableSelector = symbol.Overlay ?? (symbol.Table == AprsSymbolTable.Primary ? '/' : '\\');
        return (tableSelector, symbol.Code);
    }

    private static AprsSymbol DecodeSymbol(char tableSelector, char symbolCode)
    {
        return tableSelector switch
        {
            '/' => new AprsSymbol { Table = AprsSymbolTable.Primary, Code = symbolCode },
            '\\' => new AprsSymbol { Table = AprsSymbolTable.Secondary, Code = symbolCode },
            _ => new AprsSymbol { Table = AprsSymbolTable.Secondary, Code = symbolCode, Overlay = tableSelector },
        };
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Protocols.Aprs;

using AetherAprs.Models;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
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

    public static AprsEntry Decode(string source, string destination, IReadOnlyList<string> path, string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            throw new InvalidOperationException("APRS payload is required.");
        }

        return payload[0] switch
        {
            '!' or '=' => DecodeStation(source, destination, path, payload, hasTimestamp: false),
            '/' or '@' => DecodeStation(source, destination, path, payload, hasTimestamp: true),
            ';' => DecodeObject(source, destination, path, payload),
            ')' => DecodeItem(source, destination, path, payload),
            ':' => DecodeMessage(source, destination, path, payload),
            _ => throw new InvalidOperationException($"Unsupported APRS payload type '{payload[0]}'."),
        };
    }

    private static string EncodeStation(AprsEntry entry)
    {
        var prefix = entry.Timestamp is null ? '!' : '/';
        var timestamp = entry.Timestamp is null ? string.Empty : FormatTimestamp(entry.Timestamp.Value);
        var (symbolTable, symbolCode) = EncodeSymbol(entry.Symbol!);
        var coordinates = FormatCoordinates(entry.Location!, symbolTable, symbolCode);
        return $"{prefix}{timestamp}{coordinates}{entry.Comment ?? string.Empty}";
    }

    private static string EncodeObject(AprsEntry entry)
    {
        var (symbolTable, symbolCode) = EncodeSymbol(entry.Symbol!);
        var name = entry.ObjectName!.PadRight(9).Substring(0, 9);
        var state = entry.IsAlive ? '*' : '_';
        var coordinates = FormatCoordinates(entry.Location!, symbolTable, symbolCode);
        return $";{name}{state}{FormatTimestamp(entry.Timestamp!.Value)}{coordinates}{entry.Comment ?? string.Empty}";
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
        var recipient = entry.MessageRecipient!.PadRight(9).Substring(0, 9);
        var id = entry.MessageId is null ? string.Empty : $"{{{entry.MessageId}";
        return $":{recipient}:{entry.MessageText!}{id}";
    }

    private static AprsEntry DecodeStation(string source, string destination, IReadOnlyList<string> path, string payload, bool hasTimestamp)
    {
        var offset = 1;
        DateTimeOffset? timestamp = null;

        if (hasTimestamp)
        {
            timestamp = ParseTimestamp(payload.Substring(offset, 7));
            offset += 7;
        }

        var (location, symbol, consumed) = ParseCoordinates(payload, offset);
        var comment = payload[(offset + consumed)..];

        return new AprsEntry
        {
            Kind = AprsEntryKind.Station,
            Source = source,
            Destination = destination,
            Path = path,
            Location = location,
            Symbol = symbol,
            Timestamp = timestamp,
            Comment = string.IsNullOrEmpty(comment) ? null : comment,
        };
    }

    private static AprsEntry DecodeObject(string source, string destination, IReadOnlyList<string> path, string payload)
    {
        if (payload.Length < 18)
        {
            throw new InvalidOperationException("Object payload is too short.");
        }

        var name = payload.Substring(1, 9).TrimEnd();
        var isAlive = payload[10] == '*';
        var timestamp = ParseTimestamp(payload.Substring(11, 7));
        var (location, symbol, consumed) = ParseCoordinates(payload, 18);
        var comment = payload[(18 + consumed)..];

        return new AprsEntry
        {
            Kind = AprsEntryKind.Object,
            Source = source,
            Destination = destination,
            Path = path,
            ObjectName = name,
            IsAlive = isAlive,
            Timestamp = timestamp,
            Location = location,
            Symbol = symbol,
            Comment = string.IsNullOrEmpty(comment) ? null : comment,
        };
    }

    private static AprsEntry DecodeItem(string source, string destination, IReadOnlyList<string> path, string payload)
    {
        var markerIndex = payload.IndexOf("!", StringComparison.Ordinal);
        var deadIndex = payload.IndexOf("_", StringComparison.Ordinal);
        var separatorIndex = markerIndex >= 0 ? markerIndex : deadIndex;

        if (separatorIndex < 2)
        {
            throw new InvalidOperationException("Item payload is missing its name separator.");
        }

        var name = payload[1..separatorIndex];
        var isAlive = payload[separatorIndex] == '!';
        var (location, symbol, consumed) = ParseCoordinates(payload, separatorIndex + 1);
        var comment = payload[(separatorIndex + 1 + consumed)..];

        return new AprsEntry
        {
            Kind = AprsEntryKind.Item,
            Source = source,
            Destination = destination,
            Path = path,
            ItemName = name,
            IsAlive = isAlive,
            Location = location,
            Symbol = symbol,
            Comment = string.IsNullOrEmpty(comment) ? null : comment,
        };
    }

    private static AprsEntry DecodeMessage(string source, string destination, IReadOnlyList<string> path, string payload)
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
            Source = source,
            Destination = destination,
            Path = path,
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
        var hemisphere = latitude >= 0 ? 'N' : 'S';
        var absoluteLatitude = Math.Abs(latitude);
        var degrees = (int)Math.Floor(absoluteLatitude);
        var minutes = (absoluteLatitude - degrees) * 60d;
        return string.Format(CultureInfo.InvariantCulture, "{0:00}{1:00.00}{2}", degrees, minutes, hemisphere);
    }

    private static string FormatLongitude(double longitude)
    {
        var hemisphere = longitude >= 0 ? 'E' : 'W';
        var absoluteLongitude = Math.Abs(longitude);
        var degrees = (int)Math.Floor(absoluteLongitude);
        var minutes = (absoluteLongitude - degrees) * 60d;
        return string.Format(CultureInfo.InvariantCulture, "{0:000}{1:00.00}{2}", degrees, minutes, hemisphere);
    }

    private static double ParseLatitude(string latitude)
    {
        var degrees = double.Parse(latitude[..2], CultureInfo.InvariantCulture);
        var minutes = double.Parse(latitude[2..7], CultureInfo.InvariantCulture);
        var value = degrees + (minutes / 60d);
        return latitude[7] == 'S' ? -value : value;
    }

    private static double ParseLongitude(string longitude)
    {
        var degrees = double.Parse(longitude[..3], CultureInfo.InvariantCulture);
        var minutes = double.Parse(longitude[3..8], CultureInfo.InvariantCulture);
        var value = degrees + (minutes / 60d);
        return longitude[8] == 'W' ? -value : value;
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        return utc.ToString("ddHHmm'z'", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseTimestamp(string timestamp)
    {
        if (timestamp.Length != 7 || timestamp[6] != 'z')
        {
            throw new InvalidOperationException("Only zulu APRS timestamps are supported.");
        }

        var day = int.Parse(timestamp[..2], CultureInfo.InvariantCulture);
        var hour = int.Parse(timestamp.Substring(2, 2), CultureInfo.InvariantCulture);
        var minute = int.Parse(timestamp.Substring(4, 2), CultureInfo.InvariantCulture);
        var now = DateTimeOffset.UtcNow;
        var safeDay = Math.Min(day, DateTime.DaysInMonth(now.Year, now.Month));
        return new DateTimeOffset(now.Year, now.Month, safeDay, hour, minute, 0, TimeSpan.Zero);
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

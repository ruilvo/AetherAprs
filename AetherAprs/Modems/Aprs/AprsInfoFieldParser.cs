// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AetherAprs.Modems.Aprs;

/// <summary>
/// Static parser for APRS info fields. Decodes the APRS application-layer
/// info field string into typed packet objects.
/// </summary>
/// <remarks>
/// This handles only the APRS info field format (e.g. <c>!DDMM.mmX/DDDMM.mmYC...</c>).
/// For AX.25 frame decoding, use <see cref="Ax25Parser"/>.
/// </remarks>
public static class AprsInfoFieldParser
{
    /// <summary>
    /// Parses an APRS info field string into a typed <see cref="AprsPacket"/>.
    /// </summary>
    /// <param name="info">The APRS info field string.</param>
    /// <param name="source">The source callsign.</param>
    /// <param name="destination">The destination callsign.</param>
    /// <returns>A typed APRS packet.</returns>
    public static AprsPacket ParseInfoField(string info, Callsign source, Callsign destination)
    {
        if (string.IsNullOrEmpty(info))
        {
            return new UnknownPacket
            {
                Source = source,
                Destination = destination,
                Raw = info ?? string.Empty
            };
        }

        char typeId = info[0];

        return typeId switch
        {
            '!' or '=' => ParsePosition(info, source, destination, hasTimestamp: false),
            '@' or '/' => ParsePosition(info, source, destination, hasTimestamp: true),
            ';' => ParseObjectPosition(info, source, destination),
            ':' => ParseMessage(info, source, destination),
            '>' => ParseStatus(info, source, destination),
            '_' => ParseWeather(info, source, destination),
            '<' => ParseCapabilities(info, source, destination),
            'T' => info.Length > 1 && info[1] == '#' 
                ? ParseTelemetry(info, source, destination) 
                : new UnknownPacket { Source = source, Destination = destination, Raw = info },
            '\'' or '`' => ParseMicE(info, source, destination),
            _ => new UnknownPacket
            {
                Source = source,
                Destination = destination,
                Raw = info
            }
        };
    }

    // ---------------------------------------------------------------
    // Position parser
    // ---------------------------------------------------------------

    private static AprsPacket ParsePosition(string info, Callsign source, Callsign dest, bool hasTimestamp)
    {
        int pos = 1; // skip type identifier

        // Skip timestamp if present: @DDHHMMz/ or /DDHHMMz/
        if (hasTimestamp)
        {
            if (pos + 7 >= info.Length)
            {
                return AsUnknown(info, source, dest);
            }

            pos += 7; // skip DDHHMMz or DDHHMM/ (7 chars)
        }

        // Prefer compressed position (aprslib-style) before uncompressed.
        if (TryParseCompressedPosition(info, pos, source, dest, out var compressed))
        {
            return compressed;
        }

        // Parse latitude: DDMM.mmX (variable length, find direction char)
        int latEnd = pos;
        while (latEnd < info.Length && info[latEnd] != 'N' && info[latEnd] != 'S')
        {
            latEnd++;
        }

        if (latEnd >= info.Length || latEnd - pos < 6)
        {
            return AsUnknown(info, source, dest);
        }

        // Include direction char
        latEnd++;

        if (!TryParseLatitude(info.AsSpan(pos, latEnd - pos), out double latitude, out int latPrecision))
        {
            return AsUnknown(info, source, dest);
        }
        pos = latEnd;

        // Parse the separator between lat and lon.
        // '/' = primary table (no overlay)
        // '\' = alternate table (no overlay)
        // Otherwise: overlay character, optionally followed by table indicator
        // If only overlay with no table indicator, assume alternate table

        SymbolTable symbolTable;
        char? overlay = null;

        if (pos >= info.Length)
        {
            return AsUnknown(info, source, dest);
        }

        if (info[pos] == '/')
        {
            symbolTable = SymbolTable.Primary;
            pos++;
        }
        else if (info[pos] == '\\')
        {
            symbolTable = SymbolTable.Alternate;
            pos++;
        }
        else
        {
            // Overlay character before the table indicator (or longitude if no explicit table)
            overlay = info[pos];
            pos++;

            if (pos >= info.Length)
            {
                return AsUnknown(info, source, dest);
            }

            // Check if next char is explicit table indicator
            if (info[pos] == '/')
            {
                symbolTable = SymbolTable.Primary;
                pos++;
            }
            else if (info[pos] == '\\')
            {
                symbolTable = SymbolTable.Alternate;
                pos++;
            }
            else
            {
                // No explicit table indicator - longitude starts here
                // Overlay without explicit table implies alternate table
                symbolTable = SymbolTable.Alternate;
                // Don't increment pos - longitude parsing starts at current position
            }
        }

        // Parse longitude: DDDMM.mmY (variable length, find direction char)
        int lonEnd = pos;
        while (lonEnd < info.Length && info[lonEnd] != 'E' && info[lonEnd] != 'W')
        {
            lonEnd++;
        }

        if (lonEnd >= info.Length || lonEnd - pos < 7)
        {
            return AsUnknown(info, source, dest);
        }

        // Include direction char
        lonEnd++;

        if (!TryParseLongitude(info.AsSpan(pos, lonEnd - pos), out double longitude, out int lonPrecision))
        {
            return AsUnknown(info, source, dest);
        }
        pos = lonEnd;

        // Parse symbol code (1 char) and optional comment
        SymbolCode symbolCode;
        string? comment = null;

        if (pos < info.Length)
        {
            symbolCode = info[pos].ToSymbolCode();
            pos++;

            if (pos < info.Length)
            {
                comment = info[pos..];
            }
        }
        else
        {
            symbolCode = SymbolCode.Space;
        }

        return new PositionPacket
        {
            Source = source,
            Destination = dest,
            Raw = info,
            Latitude = latitude,
            Longitude = longitude,
            Symbol = new Symbol(symbolTable, symbolCode, overlay),
            Comment = comment,
            Precision = Math.Min(latPrecision, lonPrecision),
            Course = null,
            Speed = null,
            Altitude = null,
            Timestamp = null
        };
    }


    private static bool TryParseCompressedPosition(
        string info,
        int pos,
        Callsign source,
        Callsign dest,
        out PositionPacket packet)
    {
        packet = null!;

        if (info.Length - pos < 13)
        {
            return false;
        }

        // Uncompressed DDMM.mmN/S can collide with overlay+base91; prefer uncompressed.
        if (LooksLikeUncompressedPosition(info, pos))
        {
            return false;
        }

        var span = info.AsSpan(pos, 13);

        // Compressed shape: table/overlay + 8 base91 + symbol + csT (aprslib-style).
        char tableOrOverlay = span[0];
        bool isPrimary = tableOrOverlay == '/';
        bool isAlternate = tableOrOverlay == '\\';
        bool isOverlay = IsCompressedOverlayChar(tableOrOverlay);
        if (!isPrimary && !isAlternate && !isOverlay)
        {
            return false;
        }

        for (int i = 1; i <= 8; i++)
        {
            if (span[i] is < '!' or > '|')
            {
                return false;
            }
        }

        if (span[9] is < '!' or > '{')
        {
            return false;
        }

        for (int i = 10; i <= 12; i++)
        {
            if (span[i] is < ' ' or > '|')
            {
                return false;
            }
        }

        if (!TryDecodeBase91(span.Slice(1, 4), out int latN) ||
            !TryDecodeBase91(span.Slice(5, 4), out int lonN))
        {
            return false;
        }

        double latitude = 90.0 - latN / 380926.0;
        double longitude = -180.0 + lonN / 190463.0;

        SymbolTable symbolTable;
        char? overlay = null;
        if (isPrimary)
        {
            symbolTable = SymbolTable.Primary;
        }
        else if (isAlternate)
        {
            symbolTable = SymbolTable.Alternate;
        }
        else
        {
            symbolTable = SymbolTable.Alternate;
            overlay = tableOrOverlay;
        }

        SymbolCode symbolCode = span[9].ToSymbolCode();

        double? course = null;
        double? speed = null;
        double? altitude = null;

        int c1 = span[10] - 33;
        int s1 = span[11] - 33;
        int ctype = span[12] - 33;

        // Space char yields -1; skip course/speed/altitude when either is missing.
        if (c1 != -1 && s1 != -1)
        {
            if ((ctype & 0x18) == 0x10)
            {
                altitude = Math.Pow(1.002, c1 * 91 + s1);
            }
            else if (c1 is >= 0 and <= 89)
            {
                course = c1 == 0 ? 360 : c1 * 4.0;
                speed = Math.Pow(1.08, s1) - 1.0;
            }
        }

        string? comment = null;
        if (info.Length > pos + 13)
        {
            var remaining = info[(pos + 13)..];
            if (remaining.Length > 0)
            {
                comment = remaining;
            }
        }

        packet = new PositionPacket
        {
            Source = source,
            Destination = dest,
            Raw = info,
            Latitude = latitude,
            Longitude = longitude,
            Symbol = new Symbol(symbolTable, symbolCode, overlay),
            Comment = comment,
            Precision = 3,
            Course = course,
            Speed = speed,
            Altitude = altitude,
            Timestamp = null
        };
        return true;
    }


    private static bool LooksLikeUncompressedPosition(string info, int pos)
    {
        if (info.Length - pos < 8 || !char.IsDigit(info[pos]) || !char.IsDigit(info[pos + 1]))
        {
            return false;
        }

        int limit = Math.Min(info.Length, pos + 12);
        for (int i = pos + 4; i < limit; i++)
        {
            if (info[i] is not ('N' or 'S'))
            {
                continue;
            }

            for (int j = pos + 2; j < i; j++)
            {
                if (info[j] == '.')
                {
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    private static bool IsCompressedOverlayChar(char c) =>
        c is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'j');

    private static AprsPacket ParseObjectPosition(string info, Callsign source, Callsign destination)
    {
        // Object format: ;objectnam*DDHHMMz<position> (or '_' for a killed object).
        if (info.Length < 19 || (info[10] != '*' && info[10] != '_'))
        {
            return AsUnknown(info, source, destination);
        }

        var positionInfo = "@" + info[11..];
        var packet = ParsePosition(positionInfo, source, destination, hasTimestamp: true);

        return packet is PositionPacket
            ? packet with { Raw = info }
            : packet;
    }

    private static bool TryParseLatitude(ReadOnlySpan<char> segment, out double latitude, out int precision)
    {
        latitude = 0;
        precision = 0;

        // Format: DDMM.mmX
        if (segment.Length < 7) { return false; }

        // Degrees: 2 chars
        if (!int.TryParse(segment[..2], NumberStyles.None, CultureInfo.InvariantCulture, out int deg) ||
            deg < 0 || deg > 90)
        {
            return false;
        }

        // Minutes: 2 chars
        if (!int.TryParse(segment.Slice(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int min) ||
            min < 0 || min >= 60)
        {
            return false;
        }

        // Decimal point expected at position 4
        if (segment[4] != '.') { return false; }

        // Decimal minutes digits (until the direction char)
        int decEnd = 5;
        while (decEnd < segment.Length && char.IsDigit(segment[decEnd]))
        {
            decEnd++;
        }

        precision = decEnd - 5;
        if (precision == 0) { return false; }

        var decimalPart = segment[5..decEnd];
        if (!int.TryParse(decimalPart, NumberStyles.None, CultureInfo.InvariantCulture, out int decValue))
        {
            return false;
        }

        if (deg == 90 && (min != 0 || decValue != 0))
        {
            return false;
        }

        // Direction: last char
        char dir = segment[^1];
        if (dir != 'N' && dir != 'S') { return false; }

        double minuteDec = min + decValue / Math.Pow(10, precision);
        latitude = deg + minuteDec / 60.0;
        if (dir == 'S') { latitude = -latitude; }

        return true;
    }

    private static bool TryParseLongitude(ReadOnlySpan<char> segment, out double longitude, out int precision)
    {
        longitude = 0;
        precision = 0;

        // Format: DDDMM.mmY
        if (segment.Length < 8) { return false; }

        // Degrees: 3 chars
        if (!int.TryParse(segment[..3], NumberStyles.None, CultureInfo.InvariantCulture, out int deg) ||
            deg < 0 || deg > 180)
        {
            return false;
        }

        // Minutes: 2 chars
        if (!int.TryParse(segment.Slice(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int min) ||
            min < 0 || min >= 60)
        {
            return false;
        }

        // Decimal point expected at position 5
        if (segment[5] != '.') { return false; }

        // Decimal minutes digits (until the direction char)
        int decEnd = 6;
        while (decEnd < segment.Length && char.IsDigit(segment[decEnd]))
        {
            decEnd++;
        }

        precision = decEnd - 6;
        if (precision == 0) { return false; }

        var decimalPart = segment[6..decEnd];
        if (!int.TryParse(decimalPart, NumberStyles.None, CultureInfo.InvariantCulture, out int decValue))
        {
            return false;
        }

        if (deg == 180 && (min != 0 || decValue != 0))
        {
            return false;
        }

        // Direction: last char
        char dir = segment[^1];
        if (dir != 'E' && dir != 'W') { return false; }

        double minuteDec = min + decValue / Math.Pow(10, precision);
        longitude = deg + minuteDec / 60.0;
        if (dir == 'W') { longitude = -longitude; }

        return true;
    }


    private static bool TryDecodeBase91(ReadOnlySpan<char> chars, out int value)
    {
        value = 0;
        foreach (char c in chars)
        {
            if (c is < '!' or > '|')
            {
                value = 0;
                return false;
            }

            value = value * 91 + (c - 33);
        }

        return true;
    }

    // ---------------------------------------------------------------
    // Message parser
    // ---------------------------------------------------------------

    private static AprsPacket ParseMessage(string info, Callsign source, Callsign dest)
    {
        // Format: :ADDRESSEE :message text{msgid}
        // ACK format: :ADDRESSEE :ack{msgid}
        // REJ format: :ADDRESSEE :rej{msgid}
        var addresseeStr = "APRS";
        var addrEnd = info.IndexOf(':', 1);

        if (addrEnd > 1)
        {
            var addrLength = Math.Min(9, addrEnd - 1);
            addresseeStr = info.AsSpan(1, addrLength).TrimEnd().ToString();
        }

        if (string.IsNullOrEmpty(addresseeStr))
        {
            addresseeStr = "APRS";
        }

        var addresseeParts = addresseeStr.Split('-', 2, StringSplitOptions.None);
        var addressee = new Callsign(
            addresseeParts[0],
            addresseeParts.Length == 2 && int.TryParse(addresseeParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var addresseeSsid)
                ? addresseeSsid
                : null);

        var textStart = addrEnd >= 0 ? addrEnd + 1 : info.Length;
        var text = textStart < info.Length ? info[textStart..] : string.Empty;

        // Check for ACK format: ack{msgid} or ackN
        if (text.StartsWith("ack", StringComparison.OrdinalIgnoreCase))
        {
            var ackNumStr = text[3..];
            if (ackNumStr.StartsWith('{') && ackNumStr.EndsWith('}') && ackNumStr.Length > 2)
            {
                ackNumStr = ackNumStr[1..^1];
            }
            
            if (int.TryParse(ackNumStr, NumberStyles.None, CultureInfo.InvariantCulture, out var ackNum))
            {
                return new MessageAckPacket
                {
                    Source = source,
                    Destination = dest,
                    Raw = info,
                    Addressee = addressee,
                    MessageNumber = ackNum
                };
            }
        }

        // Check for REJ format: rej{msgid} or rejN
        if (text.StartsWith("rej", StringComparison.OrdinalIgnoreCase))
        {
            var rejNumStr = text[3..];
            if (rejNumStr.StartsWith('{') && rejNumStr.EndsWith('}') && rejNumStr.Length > 2)
            {
                rejNumStr = rejNumStr[1..^1];
            }
            
            if (int.TryParse(rejNumStr, NumberStyles.None, CultureInfo.InvariantCulture, out var rejNum))
            {
                return new MessageRejPacket
                {
                    Source = source,
                    Destination = dest,
                    Raw = info,
                    Addressee = addressee,
                    MessageNumber = rejNum
                };
            }
        }

        int? msgNumber = null;
        if (text.Length > 0)
        {
            var braceStart = text.LastIndexOf('{');
            if (braceStart >= 0 && braceStart <= text.Length - 2 && text[^1] == '}')
            {
                var suffix = text.AsSpan(braceStart + 1, text.Length - braceStart - 2);
                if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    msgNumber = number;
                    text = text[..braceStart];
                }
            }

            // Check ack format: ...}{n}
            if (msgNumber is null && text.EndsWith('}'))
            {
                var braceOpen = text.LastIndexOf('{');
                if (braceOpen > 0 && braceOpen < text.Length - 2 && text[braceOpen - 1] == '}')
                {
                    var ackNumber = text.AsSpan(braceOpen + 1, text.Length - braceOpen - 2);
                    if (int.TryParse(ackNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                    {
                        msgNumber = number;
                        text = text[..(braceOpen - 1)];
                    }
                }
            }
        }

        return new MessagePacket
        {
            Source = source,
            Destination = dest,
            Raw = info,
            Addressee = addressee,
            Text = text,
            MessageNumber = msgNumber
        };
    }

    // ---------------------------------------------------------------
    // Status parser
    // ---------------------------------------------------------------

    private static StatusPacket ParseStatus(string info, Callsign source, Callsign dest)
    {
        string text = info.Length > 1 ? info[1..] : string.Empty;

        return new StatusPacket
        {
            Source = source,
            Destination = dest,
            Raw = info,
            Text = text
        };
    }

    // ---------------------------------------------------------------
    // Weather parser
    // ---------------------------------------------------------------

    private static WeatherPacket ParseWeather(string info, Callsign source, Callsign dest)
    {
        var w = new WeatherPacket
        {
            Source = source,
            Destination = dest,
            Raw = info
        };

        if (info.Length < 2)
        {
            return w;
        }

        var data = info.AsSpan(1);

        int idx = 0;
        while (idx < data.Length)
        {
            char fieldId = data[idx];
            idx++;

            if (idx >= data.Length || !char.IsDigit(data[idx]))
            {
                continue;
            }

            int numStart = idx;
            while (idx < data.Length && (char.IsDigit(data[idx]) || data[idx] == '.'))
            {
                idx++;
            }

            var numStr = data[numStart..idx];
            if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                w = ApplyWeatherField(w, fieldId, value);
            }
        }

        return w;
    }

    private static WeatherPacket ApplyWeatherField(WeatherPacket w, char fieldId, double value)
    {
        return fieldId switch
        {
            'c' => w with { WindDirection = value },
            's' => w with { WindSpeed = value },
            'g' => w with { WindGust = value },
            't' => w with { Temperature = value },
            'h' => w with { Humidity = value },
            'b' => w with { Pressure = value },
            'r' => w with { Rain1h = value },
            'p' => w with { Rain24h = value },
            _ => w
        };
    }

    // ---------------------------------------------------------------
    // MIC-E parser
    // ---------------------------------------------------------------

    private static AprsPacket ParseMicE(string info, Callsign source, Callsign dest)
    {
        // MIC-E format: '`<longitude><speed/course><symbol table><symbol code><altitude?>
        // or '<longitude><speed/course><symbol table><symbol code><altitude?>
        // Latitude is encoded in the destination address
        
        if (info.Length < 9)
        {
            return AsUnknown(info, source, dest);
        }

        // Decode latitude from destination address
        if (!TryDecodeMicELatitude(dest.Base, out double latitude, out bool isNorth, out int offset))
        {
            return AsUnknown(info, source, dest);
        }

        int pos = 1; // Skip data type indicator (' or `)

        // Decode longitude (3 bytes)
        if (pos + 3 > info.Length)
        {
            return AsUnknown(info, source, dest);
        }

        int lonDeg = info[pos] - 28 + offset;
        if (lonDeg >= 180 && lonDeg <= 189)
        {
            lonDeg -= 80;
        }
        else if (lonDeg >= 190 && lonDeg <= 199)
        {
            lonDeg -= 190;
        }

        int lonMin = info[pos + 1] - 28;
        if (lonMin >= 60)
        {
            lonMin -= 60;
        }

        int lonHundredths = info[pos + 2] - 28;
        pos += 3;

        double longitude = lonDeg + (lonMin + lonHundredths / 100.0) / 60.0;

        // Determine if longitude is West (message bit in dest address)
        bool isWest = (dest.Base[3] >= 'P');
        if (isWest)
        {
            longitude = -longitude;
        }

        // Decode speed and course (3 bytes)
        if (pos + 3 > info.Length)
        {
            return AsUnknown(info, source, dest);
        }

        int sp = info[pos] - 28;
        int dc = info[pos + 1] - 28;
        int se = info[pos + 2] - 28;
        pos += 3;

        double? speed = null;
        double? course = null;

        int speedVal = (sp * 10) + (dc / 10);
        if (speedVal >= 0 && speedVal <= 799)
        {
            speed = speedVal * 1.852; // Convert knots to km/h (APRS typically uses knots)
        }

        int courseVal = ((dc % 10) * 100) + se;
        if (courseVal >= 0 && courseVal <= 360)
        {
            course = courseVal == 0 ? 360 : courseVal;
        }

        // Symbol table and code
        if (pos + 2 > info.Length)
        {
            return AsUnknown(info, source, dest);
        }

        char symbolTableChar = info[pos];
        char symbolCodeChar = info[pos + 1];
        pos += 2;

        SymbolTable symbolTable = symbolTableChar == '/' ? SymbolTable.Primary : SymbolTable.Alternate;
        SymbolCode symbolCode = symbolCodeChar.ToSymbolCode();

        // Optional altitude and comment
        string? comment = null;
        double? altitude = null;

        if (pos < info.Length)
        {
            comment = info[pos..];

            // Try to extract altitude from comment (format: }xxxyyy where xxx is base-91 altitude)
            if (comment.Length >= 6 && comment[0] == '}')
            {
                var altSpan = comment.AsSpan(1, 3);
                if (TryDecodeBase91(altSpan, out int altFeet))
                {
                    altitude = altFeet - 10000; // MIC-E altitude offset
                }
            }
        }

        return new PositionPacket
        {
            Source = source,
            Destination = dest,
            Raw = info,
            Latitude = latitude,
            Longitude = longitude,
            Symbol = new Symbol(symbolTable, symbolCode, null),
            Comment = comment,
            Precision = 2, // MIC-E provides ~2 decimal places
            Course = course,
            Speed = speed,
            Altitude = altitude,
            Timestamp = null
        };
    }

    private static bool TryDecodeMicELatitude(string destAddress, out double latitude, out bool isNorth, out int offset)
    {
        latitude = 0;
        isNorth = true;
        offset = 0;

        if (destAddress.Length < 6)
        {
            return false;
        }

        // MIC-E latitude encoding in destination address (6 characters)
        // Each character encodes a digit and metadata bits
        var digits = new int[6];
        
        for (int i = 0; i < 6; i++)
        {
            char c = destAddress[i];
            
            if (c >= '0' && c <= '9')
            {
                digits[i] = c - '0';
            }
            else if (c >= 'A' && c <= 'J')
            {
                digits[i] = c - 'A';
            }
            else if (c >= 'P' && c <= 'Y')
            {
                digits[i] = c - 'P';
            }
            else if (c == 'K' || c == 'L' || c == 'Z')
            {
                digits[i] = 0; // Space
            }
            else
            {
                return false;
            }
        }

        // Extract metadata from character ranges
        // Character 4 (index 3) determines N/S
        isNorth = destAddress[3] <= 'L';

        // Character 5 (index 4) determines longitude offset
        offset = (destAddress[4] >= 'P') ? 100 : 0;

        // Build latitude: DD MM.HH
        int degrees = digits[0] * 10 + digits[1];
        int minutes = digits[2] * 10 + digits[3];
        int hundredths = digits[4] * 10 + digits[5];

        if (degrees > 90 || minutes >= 60 || hundredths >= 100)
        {
            return false;
        }

        latitude = degrees + (minutes + hundredths / 100.0) / 60.0;
        
        if (!isNorth)
        {
            latitude = -latitude;
        }

        return true;
    }

    // ---------------------------------------------------------------
    // Capabilities parser
    // ---------------------------------------------------------------

    private static CapabilitiesPacket ParseCapabilities(string info, Callsign source, Callsign dest)
    {
        string text = info.Length > 1 ? info[1..] : string.Empty;

        return new CapabilitiesPacket
        {
            Source = source,
            Destination = dest,
            Raw = info,
            Text = text
        };
    }

    // ---------------------------------------------------------------
    // Telemetry parser
    // ---------------------------------------------------------------

    private static AprsPacket ParseTelemetry(string info, Callsign source, Callsign dest)
    {
        // Format: T#nnn,v1,v2,v3,v4,v5,bbbbbbbb
        if (info.Length < 3)
        {
            return AsUnknown(info, source, dest);
        }

        var parts = info[2..].Split(',');
        if (parts.Length < 1)
        {
            return AsUnknown(info, source, dest);
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int seqNum))
        {
            return AsUnknown(info, source, dest);
        }

        var analogValues = new List<double>();
        for (int i = 1; i < Math.Min(parts.Length, 6); i++)
        {
            if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                analogValues.Add(value);
            }
        }

        byte? digitalValue = null;
        if (parts.Length > 6 && parts[6].Length > 0)
        {
            // Digital value is 8 bits, usually represented as binary string or hex
            if (parts[6].Length == 8 && parts[6].All(c => c is '0' or '1'))
            {
                digitalValue = Convert.ToByte(parts[6], 2);
            }
        }

        return new TelemetryPacket
        {
            Source = source,
            Destination = dest,
            Raw = info,
            SequenceNumber = seqNum,
            AnalogValues = analogValues,
            DigitalValue = digitalValue
        };
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static UnknownPacket AsUnknown(string info, Callsign source, Callsign dest)
    {
        return new UnknownPacket
        {
            Source = source,
            Destination = dest,
            Raw = info
        };
    }
}

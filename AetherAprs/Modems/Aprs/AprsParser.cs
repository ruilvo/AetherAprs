// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using System.Text;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Modems.Aprs;

/// <summary>
/// Static parser for APRS packets. Decodes AX.25 UI-frames and parses
/// the APRS application-layer info field into typed packet objects.
/// </summary>
public static class AprsParser
{
    /// <summary>
    /// Parses a raw AX.25 UI-frame byte array into a typed <see cref="IAprsPacket"/>.
    /// The input is the complete KISS data payload (command + addresses + control + PID + info).
    /// </summary>
    /// <param name="ax25Data">The AX.25 frame payload.</param>
    /// <returns>A typed APRS packet.</returns>
    /// <exception cref="ArgumentException">Thrown if the data is too short or malformed.</exception>
    public static IAprsPacket ParseFrame(byte[] ax25Data)
    {
        var (source, dest, infoBytes) = DecodeAx25(ax25Data);
        var info = Encoding.ASCII.GetString(infoBytes);
        return ParseInfoField(info, source, dest);
    }

    /// <summary>
    /// Parses an APRS info field string into a typed packet.
    /// </summary>
    public static IAprsPacket ParseInfoField(string info, Callsign source, Callsign dest)
    {
        if (string.IsNullOrEmpty(info))
        {
            return new UnknownPacket
            {
                Source = source,
                Destination = dest,
                Raw = info ?? string.Empty
            };
        }

        char typeId = info[0];

        return typeId switch
        {
            '!' or '=' => ParsePosition(info, source, dest, hasTimestamp: false),
            '@' or '/' => ParsePosition(info, source, dest, hasTimestamp: true),
            ':' => ParseMessage(info, source, dest),
            '>' => ParseStatus(info, source, dest),
            '_' => ParseWeather(info, source, dest),
            _ => new UnknownPacket
            {
                Source = source,
                Destination = dest,
                Raw = info
            }
        };
    }

    // ---------------------------------------------------------------
    // AX.25 UI-frame decoder
    // ---------------------------------------------------------------

    private static (Callsign Source, Callsign Destination, byte[] Info) DecodeAx25(byte[] data)
    {
        if (data.Length < 15)
        {
            throw new ArgumentException($"AX.25 frame too short: {data.Length} bytes.", nameof(data));
        }

        var destination = DecodeAddress(data, 0);
        var source = DecodeAddress(data, 7);

        // Walk through address fields to find where the control field starts.
        // Addresses are 7 bytes each; the last byte of each address has
        // bit 0 (the HDLC extension bit) = 1 for the final address in the list.
        int addrEnd = 14;
        while (addrEnd < data.Length)
        {
            if ((data[addrEnd - 1] & 0x01) != 0)
            {
                break;
            }

            addrEnd += 7;
        }

        if (addrEnd >= data.Length)
        {
            throw new ArgumentException("Missing control field in AX.25 frame.");
        }

        // PID field follows control field
        int pidOffset = addrEnd + 1;
        if (pidOffset >= data.Length)
        {
            throw new ArgumentException("Missing PID field in AX.25 frame.");
        }

        // Info field
        int infoOffset = pidOffset + 1;
        int infoLength = data.Length - infoOffset;
        if (infoLength <= 0)
        {
            // Empty info field — return UnknownPacket
            return (source, destination, []);
        }

        var info = new byte[infoLength];
        Array.Copy(data, infoOffset, info, 0, infoLength);

        return (source, destination, info);
    }

    private static Callsign DecodeAddress(byte[] data, int offset)
    {
        // 6 bytes of callsign characters (ASCII, left-shifted by 1)
        Span<char> chars = stackalloc char[6];
        for (int i = 0; i < 6; i++)
        {
            chars[i] = (char)(data[offset + i] >> 1);
        }

        var @base = new string(chars).TrimEnd();

        // SSID byte: bits 1-4 contain the SSID (shifted left by 1)
        int ssidByte = data[offset + 6];
        int ssid = (ssidByte >> 1) & 0x0F;

        return new Callsign(@base, ssid == 0 ? null : ssid);
    }

    // ---------------------------------------------------------------
    // Position parser
    // ---------------------------------------------------------------

    private static IAprsPacket ParsePosition(string info, Callsign source, Callsign dest, bool hasTimestamp)
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
        // Otherwise: overlay character, read the actual table from the next char

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
            // Overlay character before the table indicator
            overlay = info[pos];
            pos++;

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
                return AsUnknown(info, source, dest);
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

        // Direction: last char
        char dir = segment[^1];
        if (dir != 'E' && dir != 'W') { return false; }

        double minuteDec = min + decValue / Math.Pow(10, precision);
        longitude = deg + minuteDec / 60.0;
        if (dir == 'W') { longitude = -longitude; }

        return true;
    }

    // ---------------------------------------------------------------
    // Message parser
    // ---------------------------------------------------------------

    private static MessagePacket ParseMessage(string info, Callsign source, Callsign dest)
    {
        // Format: :ADDRESSEE :message text{msgid}
        // Addressee is up to 9 characters (space-padded), followed by ':'
        var addresseeStr = "APRS";

        if (info.Length > 1)
        {
            // Find the colon that terminates the addressee
            int addrEnd = info.IndexOf(':', 1);
            int addrLen;

            if (addrEnd > 1 && addrEnd <= 10)
            {
                // Properly terminated: :ADDRESSEE:
                addrLen = addrEnd - 1;
                addresseeStr = info.AsSpan(1, addrLen).TrimEnd().ToString();
            }
            else if (addrEnd > 10)
            {
                // Colon exists but past 9 chars — take first 9 chars
                addrLen = Math.Min(9, info.Length - 1);
                addresseeStr = info.AsSpan(1, addrLen).TrimEnd().ToString();
            }
            else
            {
                // No terminating colon found — take what we can (up to 9 chars)
                addrLen = Math.Min(9, info.Length - 1);
                addresseeStr = info.AsSpan(1, addrLen).TrimEnd().ToString();
            }

            if (string.IsNullOrEmpty(addresseeStr))
            {
                addresseeStr = "APRS";
            }
        }

        var addressee = new Callsign(addresseeStr, null);

        // Message text starts after the addressee field and its terminating ':'
        string text;
        int textStart = info.Length > 1 ? info.IndexOf(':', 1) + 1 : info.Length;
        text = (textStart > 0 && textStart < info.Length) ? info[textStart..] : string.Empty;

        // Check for message number suffix {nn}
        int? msgNumber = null;
        if (text.Length > 0)
        {
            int braceStart = text.LastIndexOf('{');
            if (braceStart >= 0 && braceStart <= text.Length - 2 && text[^1] == '}')
            {
                var suffix = text.AsSpan(braceStart + 1, text.Length - braceStart - 2);
                if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out int num))
                {
                    msgNumber = num;
                    text = text[..braceStart];
                }
            }

            // Check ack format: ...}{n}
            if (msgNumber is null && text.EndsWith('}'))
            {
                int braceOpen = text.LastIndexOf('{');
                if (braceOpen >= 0 && braceOpen < text.Length - 2 && text[braceOpen - 1] == '}')
                {
                    var ackNum = text.AsSpan(braceOpen + 1, text.Length - braceOpen - 2);
                    if (int.TryParse(ackNum, NumberStyles.None, CultureInfo.InvariantCulture, out int ackVal))
                    {
                        msgNumber = ackVal;
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
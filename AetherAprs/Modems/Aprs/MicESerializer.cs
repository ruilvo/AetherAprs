// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text;

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;

namespace AetherAprs.Modems.Aprs;

/// <summary>
/// Encodes APRS Mic-E packets. Latitude is encoded into the AX.25 destination
/// address (<see cref="EncodeDestination"/>); longitude, course, speed, symbol
/// and optional altitude/comment are encoded into the info field
/// (<see cref="FormatInfoField"/>).
/// </summary>
/// <remarks>
/// Mic-E is a compact position format used for mobile stations. It repurposes the
/// 6-character destination address to carry latitude (plus N/S, longitude-offset,
/// and W/E flags) while the info field carries longitude and course/speed.
/// </remarks>
public static class MicESerializer
{
    // Data type identifier: '`' (0x60) = current GPS position.
    private const char CurrentPosition = '`';

    // Legacy one-character radio identifier used by the Kenwood TM-D700. The
    // altitude field follows it when present.
    private const char AltitudePrefix = ']';

    /// <summary>
    /// Encodes the latitude and its N/S, longitude-offset, and W/E flags into a
    /// 6-character destination address, as required by the Mic-E format.
    /// </summary>
    /// <param name="packet">The position packet to encode.</param>
    /// <returns>The Mic-E destination address (no SSID).</returns>
    public static Callsign EncodeDestination(PositionPacket packet)
    {
        double latitude = Math.Abs(packet.Location.Latitude);
        bool isNorth = packet.Location.Latitude >= 0;

        (int degrees, int minutes, int hundredths) = SplitDegrees(latitude);

        int longitudeDegrees = (int)Math.Abs(packet.Location.Longitude);
        bool isWest = packet.Location.Longitude < 0;
        bool offset = longitudeDegrees >= 100;

        // Position 1-3 use the '0'-'9' range (message bits clear = standard message).
        // Position 4 (N/S), 5 (longitude offset) and 6 (W/E) use '0'-'9' for the
        // default state and 'P'-'Y' for the flagged state. For N/S the flagged
        // state is north, matching the parser and the APRS specification.
        char[] chars =
        [
            (char)('0' + degrees / 10),
            (char)('0' + degrees % 10),
            (char)('0' + minutes / 10),
            (char)((isNorth ? 'P' : '0') + minutes % 10),
            (char)((offset ? 'P' : '0') + hundredths / 10),
            (char)((isWest ? 'P' : '0') + hundredths % 10),
        ];
        return new Callsign(new string(chars));
    }

    /// <summary>
    /// Encodes the Mic-E info field: longitude, course/speed, symbol table and
    /// code, and optional altitude/comment.
    /// </summary>
    /// <param name="packet">The position packet to encode.</param>
    /// <returns>The Mic-E info field string.</returns>
    public static string FormatInfoField(PositionPacket packet)
    {
        double longitude = Math.Abs(packet.Location.Longitude);

        (int degrees, int minutes, int hundredths) = SplitDegrees(longitude);
        bool offset = degrees >= 100;

        StringBuilder info = new();
        info.Append(CurrentPosition);

        // Longitude degrees, encoded with the offset-flag scheme the parser
        // decodes (100-109 use a distinct range to avoid the 0-9 ambiguity).
        int degreeChar;
        if (!offset)
        {
            degreeChar = degrees + 28;      // 0-99
        }
        else if (degrees < 110)
        {
            degreeChar = degrees + 8;       // 100-109
        }
        else
        {
            degreeChar = degrees - 72;      // 110-179
        }

        info.Append((char)degreeChar);
        info.Append((char)(minutes + 28));
        info.Append((char)(hundredths + 28));

        // Course/speed: sp = tens of knots, dc = units-of-knots * 10 + hundreds
        // of course, se = course remainder (inverse of the parser's decode).
        int speed = (int)Math.Round(packet.Speed ?? 0.0, MidpointRounding.AwayFromZero);
        int course = (int)Math.Round(packet.Course ?? 0.0, MidpointRounding.AwayFromZero);
        course %= 360; // 360 == north == 0 on the wire

        info.Append((char)(speed / 10 + 28));
        info.Append((char)((speed % 10) * 10 + course / 100 + 28));
        info.Append((char)(course % 100 + 28));

        // Mic-E reverses the usual order: symbol code first, then symbol table.
        info.Append(packet.Symbol.CodeChar);
        info.Append(packet.Symbol.TableChar);

        // Optional altitude: a one-character radio identifier, three base-91
        // digits of meters plus 10000, then '}' (the order a Kenwood TM-D700 and
        // the reference decoders use).
        if (packet.Altitude.HasValue)
        {
            int meters = (int)Math.Round(
                AprsUnits.FeetToMeters(packet.Altitude.Value), MidpointRounding.AwayFromZero);
            info.Append(AltitudePrefix);
            info.Append(EncodeBase91(meters + 10000, 3));
            info.Append('}');
        }

        // Optional comment.
        if (!string.IsNullOrEmpty(packet.Comment))
        {
            info.Append(packet.Comment);
        }

        return info.ToString();
    }

    /// <summary>
    /// Splits an absolute coordinate into degrees, minutes, and hundredths of a
    /// minute, propagating rounding overflow (e.g. 59.9999 minutes -> 60.00).
    /// </summary>
    private static (int Degrees, int Minutes, int Hundredths) SplitDegrees(double value)
    {
        int degrees = (int)value;
        double minuteDecimal = (value - degrees) * 60.0;
        int minutes = (int)minuteDecimal;
        int hundredths = (int)Math.Round((minuteDecimal - minutes) * 100.0, MidpointRounding.AwayFromZero);

        if (hundredths >= 100)
        {
            hundredths = 0;
            minutes++;
        }

        if (minutes >= 60)
        {
            minutes = 0;
            degrees++;
        }

        return (degrees, minutes, hundredths);
    }

    /// <summary>
    /// Encodes an integer as a fixed number of base-91 digits (most significant
    /// first), each mapped to the printable range '!' (33) .. '{' (123).
    /// </summary>
    private static string EncodeBase91(int value, int digits)
    {
        char[] chars = new char[digits];
        for (int i = digits - 1; i >= 0; i--)
        {
            chars[i] = (char)(value % 91 + 33);
            value /= 91;
        }

        return new string(chars);
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Modems.Aprs;

/// <summary>
/// Static serializer for APRS packets. Encodes typed <see cref="IAprsPacket"/>
/// into AX.25 UI-frame byte arrays ready for KISS framing.
/// </summary>
public static class AprsSerializer
{
    private const int AddressLength = 7;
    private const byte UiControl = 0x03;
    private const byte NoLayer3Pid = 0xF0;

    /// <summary>
    /// Serializes an <see cref="IAprsPacket"/> into an AX.25 UI-frame byte array
    /// (addresses + control + PID + info), suitable for wrapping in a KISS frame.
    /// </summary>
    /// <param name="packet">The packet to serialize.</param>
    /// <param name="source">The source callsign.</param>
    /// <param name="destination">The destination callsign.</param>
    /// <returns>AX.25 frame bytes (without CRC, for KISS).</returns>
    /// <exception cref="ArgumentException">Thrown if the packet type is not supported.</exception>
    public static byte[] Serialize(IAprsPacket packet, Callsign source, Callsign destination)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return packet switch
        {
            PositionPacket pos => SerializePosition(pos, source, destination),
            MessagePacket msg => SerializeMessage(msg, source, destination),
            StatusPacket status => SerializeStatus(status, source, destination),
            WeatherPacket weather => SerializeWeather(weather, source, destination),
            UnknownPacket unknown => SerializeUnknown(unknown, source, destination),
            _ => throw new ArgumentException($"Unsupported packet type: {packet.GetType().Name}.")
        };
    }

    // ---------------------------------------------------------------
    // AX.25 framer
    // ---------------------------------------------------------------

    private static byte[] BuildAx25Frame(Callsign destination, Callsign source, string infoField)
    {
        var infoBytes = Encoding.ASCII.GetBytes(infoField);
        return BuildAx25Frame(destination, source, infoBytes);
    }

    private static byte[] BuildAx25Frame(Callsign destination, Callsign source, byte[] infoBytes)
    {
        var frame = new List<byte>();

        // Destination address (7 bytes)
        frame.AddRange(EncodeAddress(destination, isLast: false));

        // Source address (7 bytes, marked as last address in the list)
        frame.AddRange(EncodeAddress(source, isLast: true));

        // Control field: UI-frame (0x03)
        frame.Add(UiControl);

        // PID: No layer 3 (0xF0)
        frame.Add(NoLayer3Pid);

        // Info field
        frame.AddRange(infoBytes);

        return [.. frame];
    }

    private static byte[] EncodeAddress(Callsign callsign, bool isLast)
    {
        var bytes = new byte[AddressLength];

        // Pad callsign base to 6 characters with spaces
        var padded = callsign.Base.PadRight(6, ' ');

        // Each character: ASCII value left-shifted by 1
        for (int i = 0; i < 6; i++)
        {
            bytes[i] = (byte)(padded[i] << 1);
        }

        // SSID byte: bits 1-4 = SSID, bit 0 = 1 if last address (HDLC extension bit)
        int ssid = callsign.Ssid ?? 0;
        bytes[6] = (byte)((ssid << 1) | 0x60 | (isLast ? 0x01 : 0x00));

        return bytes;
    }

    // ---------------------------------------------------------------
    // Position serializer
    // ---------------------------------------------------------------

    private static byte[] SerializePosition(PositionPacket packet, Callsign source, Callsign destination)
    {
        // Format: !DDMM.mmX/DDDMM.mmYC[comment]
        // (using '!' — no timestamp, suitable for typical fixed/generic positions)
        // The '/' or '\' between lat and lon IS the symbol table indicator.
        char typeId = '!'; // simple position without timestamp

        string lat = FormatLatitude(packet.Latitude, packet.Precision);
        string lon = FormatLongitude(packet.Longitude, packet.Precision);

        var info = new StringBuilder();
        info.Append(typeId);
        info.Append(lat);
        info.Append(packet.Symbol.Table); // separator between lat and lon = symbol table
        info.Append(lon);
        info.Append(packet.Symbol.Code);

        if (!string.IsNullOrEmpty(packet.Comment))
        {
            info.Append(packet.Comment);
        }

        return BuildAx25Frame(destination, source, info.ToString());
    }

    private static string FormatLatitude(double latitude, int precision)
    {
        bool isSouth = latitude < 0;
        double absLat = Math.Abs(latitude);

        int deg = (int)absLat;
        double minDecimal = (absLat - deg) * 60.0;
        int min = (int)(minDecimal + 1e-7);
        double decMin = Math.Abs(minDecimal - min) * Math.Pow(10, precision);
        int decValue = (int)Math.Round(decMin);

        char dir = isSouth ? 'S' : 'N';

        return $"{deg:D2}{min:D2}.{decValue.ToString(new string('0', precision))}{dir}";
    }

    private static string FormatLongitude(double longitude, int precision)
    {
        bool isWest = longitude < 0;
        double absLon = Math.Abs(longitude);

        int deg = (int)absLon;
        double minDecimal = (absLon - deg) * 60.0;
        int min = (int)(minDecimal + 1e-7);
        double decMin = Math.Abs(minDecimal - min) * Math.Pow(10, precision);
        int decValue = (int)Math.Round(decMin);

        char dir = isWest ? 'W' : 'E';

        return $"{deg:D3}{min:D2}.{decValue.ToString(new string('0', precision))}{dir}";
    }

    // ---------------------------------------------------------------
    // Message serializer
    // ---------------------------------------------------------------

    private static byte[] SerializeMessage(MessagePacket packet, Callsign source, Callsign destination)
    {
        // Format: :ADDRESSEE :message text{msgid}
        var info = new StringBuilder();
        info.Append(':');
        info.Append(packet.Addressee.Base.PadRight(9, ' '));
        info.Append(':');

        if (packet.MessageNumber.HasValue)
        {
            info.Append($"{packet.Text}{{{packet.MessageNumber.Value}}}");
        }
        else
        {
            info.Append(packet.Text);
        }

        return BuildAx25Frame(destination, source, info.ToString());
    }

    // ---------------------------------------------------------------
    // Status serializer
    // ---------------------------------------------------------------

    private static byte[] SerializeStatus(StatusPacket packet, Callsign source, Callsign destination)
    {
        // Format: >status text
        var info = $">{packet.Text}";
        return BuildAx25Frame(destination, source, info);
    }

    // ---------------------------------------------------------------
    // Weather serializer
    // ---------------------------------------------------------------

    private static byte[] SerializeWeather(WeatherPacket packet, Callsign source, Callsign destination)
    {
        // Format: _c111s222g333t444h55b77777r111p222...
        // Only non-null fields are included.
        var info = new StringBuilder();
        info.Append('_');

        if (packet.WindDirection.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"c{packet.WindDirection.Value:F0}");
        if (packet.WindSpeed.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"s{packet.WindSpeed.Value:F0}");
        if (packet.WindGust.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"g{packet.WindGust.Value:F0}");
        if (packet.Temperature.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"t{packet.Temperature.Value:F0}");
        if (packet.Humidity.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"h{packet.Humidity.Value:F0}");
        if (packet.Pressure.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"b{packet.Pressure.Value:F0}");
        if (packet.Rain1h.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"r{packet.Rain1h.Value:F2}");
        if (packet.Rain24h.HasValue)
            info.Append(CultureInfo.InvariantCulture, $"p{packet.Rain24h.Value:F2}");

        return BuildAx25Frame(destination, source, info.ToString());
    }

    // ---------------------------------------------------------------
    // Unknown serializer (passthrough raw data)
    // ---------------------------------------------------------------

    private static byte[] SerializeUnknown(UnknownPacket packet, Callsign source, Callsign destination)
    {
        return BuildAx25Frame(destination, source, packet.Raw);
    }
}
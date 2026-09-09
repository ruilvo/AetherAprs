// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Text;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Modems.Aprs;

/// <summary>
/// Serializes <see cref="AprsPacket"/> into AX.25 UI-frame byte arrays
/// (addresses + control + PID + info field), suitable for KISS framing.
/// </summary>
public static class Ax25Serializer
{
    private const int AddressLength = 7;
    private const byte UiControl = 0x03;
    private const byte NoLayer3Pid = 0xF0;

    /// <summary>
    /// Serializes an <see cref="AprsPacket"/> into an AX.25 UI-frame byte array.
    /// Source and destination callsigns are read from the packet itself.
    /// </summary>
    /// <param name="packet">The packet to serialize.</param>
    /// <returns>AX.25 frame bytes (without CRC, for KISS).</returns>
    public static byte[] Serialize(AprsPacket packet)
    {
        var infoField = AprsSerializer.FormatInfoField(packet);
        return BuildAx25Frame(packet.Destination, packet.Source, infoField);
    }

    /// <summary>
    /// Serializes an <see cref="AprsPacket"/> with explicit source and destination,
    /// overriding whatever is in the packet itself.
    /// </summary>
    /// <param name="packet">The packet to serialize.</param>
    /// <param name="source">The source callsign to use in the AX.25 header.</param>
    /// <param name="destination">The destination callsign to use in the AX.25 header.</param>
    /// <returns>AX.25 frame bytes (without CRC, for KISS).</returns>
    public static byte[] Serialize(AprsPacket packet, Callsign source, Callsign destination)
    {
        var infoField = AprsSerializer.FormatInfoField(packet);
        return BuildAx25Frame(destination, source, infoField);
    }

    // ---------------------------------------------------------------
    // AX.25 framer
    // ---------------------------------------------------------------

    internal static byte[] BuildAx25Frame(Callsign destination, Callsign source, string infoField)
    {
        var infoBytes = Encoding.ASCII.GetBytes(infoField);
        return BuildAx25Frame(destination, source, infoBytes);
    }

    internal static byte[] BuildAx25Frame(Callsign destination, Callsign source, byte[] infoBytes)
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

    internal static byte[] EncodeAddress(Callsign callsign, bool isLast)
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
}
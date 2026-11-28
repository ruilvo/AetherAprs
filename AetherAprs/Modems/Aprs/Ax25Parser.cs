// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text;

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;

namespace AetherAprs.Modems.Aprs;

/// <summary>
/// Decodes AX.25 UI-frames and dispatches to <see cref="AprsInfoFieldParser.ParseInfoField"/>
/// for APRS application-layer parsing.
/// </summary>
public static class Ax25Parser
{
    /// <summary>
    /// Parses a raw AX.25 UI-frame byte array into a typed <see cref="AprsPacket"/>.
    /// The input is the complete KISS data payload (command + addresses + control + PID + info).
    /// </summary>
    /// <param name="ax25Data">The AX.25 frame payload.</param>
    /// <returns>A typed APRS packet.</returns>
    /// <exception cref="ArgumentException">Thrown if the data is too short or malformed.</exception>
    public static AprsPacket ParseFrame(byte[] ax25Data)
    {
        (Callsign source, Callsign dest, Callsign[]? path, byte[]? infoBytes) = DecodeAx25(ax25Data);
        string info = Encoding.ASCII.GetString(infoBytes);
        AprsPacket packet = AprsInfoFieldParser.ParseInfoField(info, source, dest, path);

        // Persist the raw frame (hex) and the textual path portion so downstream
        // consumers can reconstruct exactly what was received over the air.
        string pathString = path.Length == 0
            ? $"{source}>{dest}"
            : $"{source}>{dest},{PathHelper.FormatPath(path)}";

        return packet with
        {
            RawFrameHex = Convert.ToHexString(ax25Data),
            RawPath = pathString
        };
    }

    // ---------------------------------------------------------------
    // AX.25 UI-frame decoder
    // ---------------------------------------------------------------

    internal static (Callsign Source, Callsign Destination, Callsign[] Path, byte[] Info) DecodeAx25(byte[] data)
    {
        if (data.Length < 15)
        {
            throw new ArgumentException($"AX.25 frame too short: {data.Length} bytes.", nameof(data));
        }

        (Callsign destination, bool _) = DecodeAddress(data, 0);
        (Callsign source, bool _) = DecodeAddress(data, 7);

        // Walk through address fields to find where the control field starts.
        // Addresses are 7 bytes each; the last byte of each address has
        // bit 0 (the HDLC extension bit) = 1 for the final address in the list.
        List<Callsign> path = [];
        int addrEnd = 14;
        while (addrEnd < data.Length)
        {
            if ((data[addrEnd - 1] & 0x01) != 0)
            {
                break;
            }

            // There's another address (digipeater); a truncated frame must fail
            // with an ArgumentException, not an out-of-bounds read.
            if (addrEnd + 7 > data.Length)
            {
                throw new ArgumentException(
                    $"Truncated AX.25 address field at offset {addrEnd}.", nameof(data));
            }

            // Only digipeater path addresses carry the H ("has been repeated") bit.
            // The source address's bit 7 is the command/response bit, so it is
            // intentionally not surfaced as HasBeenRepeated.
            (Callsign hop, bool hasBeenRepeated) = DecodeAddress(data, addrEnd);
            path.Add(new Callsign(hop.Base, hop.Ssid, hasBeenRepeated));
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
            // Empty info field — return empty bytes
            return (source, destination, [.. path], []);
        }

        byte[] info = new byte[infoLength];
        Array.Copy(data, infoOffset, info, 0, infoLength);

        return (source, destination, [.. path], info);
    }

    internal static (Callsign Callsign, bool HasBeenRepeated) DecodeAddress(byte[] data, int offset)
    {
        // 6 bytes of callsign characters (ASCII, left-shifted by 1)
        Span<char> chars = stackalloc char[6];
        for (int i = 0; i < 6; i++)
        {
            // Map non-printable decoded characters (e.g. NUL padding) to spaces so
            // malformed wire data cannot embed control characters in the callsign
            // base, mirroring how most TNCs display padded addresses.
            char c = (char)(data[offset + i] >> 1);
            chars[i] = c >= ' ' && c < 0x7F ? c : ' ';
        }

        string callsignBase = new string(chars).TrimEnd();

        // SSID byte: bits 1-4 contain the SSID (shifted left by 1),
        // bit 7 is the "has been repeated" (H) flag.
        int ssidByte = data[offset + 6];
        int ssid = (ssidByte >> 1) & 0x0F;
        bool hasBeenRepeated = (ssidByte & 0x80) != 0;

        return (new Callsign(callsignBase, ssid == 0 ? null : ssid), hasBeenRepeated);
    }
}

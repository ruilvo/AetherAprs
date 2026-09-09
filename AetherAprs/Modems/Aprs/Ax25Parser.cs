// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Modems.Aprs;

/// <summary>
/// Decodes AX.25 UI-frames and dispatches to <see cref="AprsParser.ParseInfoField"/>
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
        var (source, dest, infoBytes) = DecodeAx25(ax25Data);
        var info = Encoding.ASCII.GetString(infoBytes);
        return AprsParser.ParseInfoField(info, source, dest);
    }

    // ---------------------------------------------------------------
    // AX.25 UI-frame decoder
    // ---------------------------------------------------------------

    internal static (Callsign Source, Callsign Destination, byte[] Info) DecodeAx25(byte[] data)
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
            // Empty info field — return empty bytes
            return (source, destination, []);
        }

        var info = new byte[infoLength];
        Array.Copy(data, infoOffset, info, 0, infoLength);

        return (source, destination, info);
    }

    internal static Callsign DecodeAddress(byte[] data, int offset)
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
}
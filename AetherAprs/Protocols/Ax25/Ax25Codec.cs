// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Protocols.Ax25;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static class Ax25Codec
{
    public const byte UiFrameControl = 0x03;
    public const byte NoLayer3ProtocolId = 0xF0;

    public static byte[] Encode(Ax25Frame frame)
    {
        var bytes = new List<byte>();
        var addresses = new List<Ax25Address> { frame.Destination, frame.Source };
        addresses.AddRange(frame.Digipeaters);

        for (var index = 0; index < addresses.Count; index++)
        {
            var address = addresses[index];
            var isLast = index == addresses.Count - 1;
            bytes.AddRange(EncodeAddress(address, isLast));
        }

        bytes.Add(frame.Control);
        bytes.Add(frame.ProtocolId);
        bytes.AddRange(frame.Information);
        return [.. bytes];
    }

    public static Ax25Frame Decode(ReadOnlySpan<byte> frameBytes)
    {
        if (frameBytes.Length < 16)
        {
            throw new InvalidOperationException("AX.25 frame is too short.");
        }

        var addresses = new List<Ax25Address>();
        var offset = 0;

        while (true)
        {
            if (frameBytes.Length < offset + 7)
            {
                throw new InvalidOperationException("AX.25 address field is truncated.");
            }

            var addressBytes = frameBytes.Slice(offset, 7);
            addresses.Add(DecodeAddress(addressBytes));
            var isLast = (addressBytes[6] & 0x01) == 0x01;
            offset += 7;

            if (isLast)
            {
                break;
            }
        }

        if (addresses.Count < 2 || frameBytes.Length < offset + 2)
        {
            throw new InvalidOperationException("AX.25 frame is missing control, PID, or mandatory addresses.");
        }

        var destination = addresses[0];
        var source = addresses[1];
        var digipeaters = addresses.Skip(2).ToArray();
        var control = frameBytes[offset];
        var protocolId = frameBytes[offset + 1];
        var information = frameBytes[(offset + 2)..].ToArray();

        return new Ax25Frame(destination, source, digipeaters, control, protocolId, information);
    }

    public static Ax25Address ParseAddress(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("AX.25 address is required.");
        }

        var repeated = text.EndsWith('*');
        var cleanText = repeated ? text[..^1] : text;
        var parts = cleanText.Split('-', 2, StringSplitOptions.TrimEntries);
        var callSign = parts[0].ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(callSign) || callSign.Length > 6)
        {
            throw new InvalidOperationException("AX.25 callsigns must be between 1 and 6 characters.");
        }

        foreach (var character in callSign)
        {
            if (!char.IsLetterOrDigit(character))
            {
                throw new InvalidOperationException("AX.25 callsigns may only contain letters and digits.");
            }
        }

        var ssid = parts.Length == 2 ? byte.Parse(parts[1]) : (byte)0;
        if (ssid > 15)
        {
            throw new InvalidOperationException("AX.25 SSID must be between 0 and 15.");
        }

        return new Ax25Address(callSign, ssid, repeated);
    }

    public static string FormatAddress(Ax25Address address)
    {
        var ssid = address.Ssid == 0 ? string.Empty : $"-{address.Ssid}";
        var repeated = address.HasBeenRepeated ? "*" : string.Empty;
        return $"{address.CallSign}{ssid}{repeated}";
    }

    private static byte[] EncodeAddress(Ax25Address address, bool isLast)
    {
        if (string.IsNullOrWhiteSpace(address.CallSign) || address.CallSign.Length > 6)
        {
            throw new InvalidOperationException("AX.25 callsigns must be between 1 and 6 characters.");
        }

        if (address.Ssid > 15)
        {
            throw new InvalidOperationException("AX.25 SSID must be between 0 and 15.");
        }

        var callSign = address.CallSign.ToUpperInvariant().PadRight(6);
        var bytes = new byte[7];

        for (var index = 0; index < 6; index++)
        {
            bytes[index] = (byte)(callSign[index] << 1);
        }

        bytes[6] = (byte)(0x60 | ((address.Ssid & 0x0F) << 1) | (isLast ? 0x01 : 0x00) | (address.HasBeenRepeated ? 0x80 : 0x00));
        return bytes;
    }

    private static Ax25Address DecodeAddress(ReadOnlySpan<byte> addressBytes)
    {
        var chars = new char[6];
        for (var index = 0; index < chars.Length; index++)
        {
            chars[index] = (char)(addressBytes[index] >> 1);
        }

        var callSign = new string(chars).TrimEnd();
        var ssid = (byte)((addressBytes[6] >> 1) & 0x0F);
        var hasBeenRepeated = (addressBytes[6] & 0x80) == 0x80;
        return new Ax25Address(callSign, ssid, hasBeenRepeated);
    }
}

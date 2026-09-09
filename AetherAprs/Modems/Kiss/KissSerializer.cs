// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace AetherAprs.Modems.Kiss;

/// <summary>
/// Provides low-level KISS frame encoding and decoding.
/// Handles FEND/FESC escaping and the raw byte-level framing format.
/// </summary>
public static class KissSerializer
{
    /// <summary>
    /// Encodes a KISS frame into a complete frame buffer (with FEND markers and escaping).
    /// </summary>
    /// <param name="frame">The frame to encode.</param>
    /// <returns>Escaped byte sequence including opening and closing FEND bytes.</returns>
    public static byte[] EncodeFrame(KissFrame frame)
    {
        var escapedData = EscapeData(frame.Data);
        var buffer = new byte[1 + 1 + escapedData.Length + 1];

        buffer[0] = KissConstants.FEND;
        buffer[1] = frame.Command;
        escapedData.CopyTo(buffer, 2);
        buffer[^1] = KissConstants.FEND;

        return buffer;
    }

    /// <summary>
    /// Decodes a raw KISS frame (without FEND markers) into a <see cref="KissFrame"/>.
    /// The provided data must already be unescaped.
    /// </summary>
    /// <param name="rawData">The unescaped frame bytes. First byte is the command.</param>
    /// <returns>The decoded frame.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawData"/> is empty.</exception>
    public static KissFrame DecodeFrame(byte[] rawData)
    {
        if (rawData.Length == 0)
        {
            throw new ArgumentException("Frame data cannot be empty.", nameof(rawData));
        }

        var command = rawData[0];
        var data = rawData.Length > 1
            ? rawData[1..]
            : [];

        return new KissFrame(command, data);
    }

    /// <summary>
    /// Escapes a data buffer by replacing FEND (0xC0) with FESC TFEND (0xDB 0xDC)
    /// and FESC (0xDB) with FESC TFESC (0xDB 0xDD).
    /// </summary>
    /// <param name="data">The raw data to escape.</param>
    /// <returns>The escaped byte sequence.</returns>
    public static byte[] EscapeData(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return [];
        }

        var result = new List<byte>(data.Length);

        foreach (var b in data)
        {
            switch (b)
            {
                case KissConstants.FEND:
                    result.Add(KissConstants.FESC);
                    result.Add(KissConstants.TFEND);
                    break;
                case KissConstants.FESC:
                    result.Add(KissConstants.FESC);
                    result.Add(KissConstants.TFESC);
                    break;
                default:
                    result.Add(b);
                    break;
            }
        }

        return [.. result];
    }

    /// <summary>
    /// Unescapes a buffer by reversing the KISS escaping rules.
    /// Replaces FESC TFEND (0xDB 0xDC) with FEND (0xC0)
    /// and FESC TFESC (0xDB 0xDD) with FESC (0xDB).
    /// </summary>
    /// <param name="data">The escaped data to unescape.</param>
    /// <returns>The unescaped byte sequence.</returns>
    /// <exception cref="ArgumentException">Thrown when an invalid escape sequence is encountered.</exception>
    public static byte[] UnescapeData(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return [];
        }

        var result = new List<byte>(data.Length);

        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == KissConstants.FESC)
            {
                i++;
                if (i >= data.Length)
                {
                    throw new ArgumentException("Incomplete escape sequence at end of data.", nameof(data));
                }

                switch (data[i])
                {
                    case KissConstants.TFEND:
                        result.Add(KissConstants.FEND);
                        break;
                    case KissConstants.TFESC:
                        result.Add(KissConstants.FESC);
                        break;
                    default:
                        throw new ArgumentException(
                            $"Invalid escape sequence: FESC followed by 0x{data[i]:X2}.", nameof(data));
                    }
                }
            else
            {
                result.Add(data[i]);
            }
        }

        return [.. result];
    }
}
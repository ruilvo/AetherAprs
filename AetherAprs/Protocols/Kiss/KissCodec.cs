// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Protocols.Kiss;

using System;
using System.Collections.Generic;

public static class KissCodec
{
    public const byte FrameEnd = 0xC0;
    public const byte FrameEscape = 0xDB;
    public const byte TransposedFrameEnd = 0xDC;
    public const byte TransposedFrameEscape = 0xDD;

    public static byte[] Encode(KissFrame frame)
    {
        var bytes = new List<byte> { FrameEnd, (byte)((frame.Port << 4) | (frame.Command & 0x0F)) };

        foreach (var value in frame.Payload)
        {
            switch (value)
            {
                case FrameEnd:
                    bytes.Add(FrameEscape);
                    bytes.Add(TransposedFrameEnd);
                    break;
                case FrameEscape:
                    bytes.Add(FrameEscape);
                    bytes.Add(TransposedFrameEscape);
                    break;
                default:
                    bytes.Add(value);
                    break;
            }
        }

        bytes.Add(FrameEnd);
        return [.. bytes];
    }

    public static KissFrame Decode(ReadOnlySpan<byte> frameBytes)
    {
        if (frameBytes.Length < 3 || frameBytes[0] != FrameEnd || frameBytes[^1] != FrameEnd)
        {
            throw new InvalidOperationException("KISS frame must start and end with FEND.");
        }

        var commandByte = frameBytes[1];
        var payload = new List<byte>();

        for (var index = 2; index < frameBytes.Length - 1; index++)
        {
            var value = frameBytes[index];
            if (value == FrameEscape)
            {
                index++;
                if (index >= frameBytes.Length - 1)
                {
                    throw new InvalidOperationException("KISS frame ends with an incomplete escape sequence.");
                }

                value = frameBytes[index] switch
                {
                    TransposedFrameEnd => FrameEnd,
                    TransposedFrameEscape => FrameEscape,
                    _ => throw new InvalidOperationException("KISS frame contains an invalid escape sequence."),
                };
            }

            payload.Add(value);
        }

        return new KissFrame((byte)(commandByte >> 4), (byte)(commandByte & 0x0F), [.. payload]);
    }
}

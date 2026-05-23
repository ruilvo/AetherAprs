// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Protocols.Ax25;

using AetherAprs.Models;
using AetherAprs.Protocols.Aprs;
using AetherAprs.Protocols.Kiss;
using System;
using System.Linq;
using System.Text;

public static class Ax25AprsKissCodec
{
    public static byte[] Encode(AprsEntry entry, byte port = 0, byte command = 0)
    {
        entry.Validate();

        var information = Encoding.ASCII.GetBytes(AprsPayloadCodec.Encode(entry));
        var frame = new Ax25Frame(
            Ax25Codec.ParseAddress(entry.Destination),
            Ax25Codec.ParseAddress(entry.Source),
            entry.Path.Select(Ax25Codec.ParseAddress).ToArray(),
            Ax25Codec.UiFrameControl,
            Ax25Codec.NoLayer3ProtocolId,
            information);

        var ax25Bytes = Ax25Codec.Encode(frame);
        return KissCodec.Encode(new KissFrame(port, command, ax25Bytes));
    }

    public static AprsEntry Decode(ReadOnlySpan<byte> kissFrame)
    {
        var decodedKissFrame = KissCodec.Decode(kissFrame);
        var ax25Frame = Ax25Codec.Decode(decodedKissFrame.Payload);
        var payload = Encoding.ASCII.GetString(ax25Frame.Information);
        var path = ax25Frame.Digipeaters.Select(Ax25Codec.FormatAddress).ToArray();
        return AprsPayloadCodec.Decode(
            Ax25Codec.FormatAddress(ax25Frame.Source),
            Ax25Codec.FormatAddress(ax25Frame.Destination),
            path,
            payload);
    }
}

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
    public static byte[] Encode(AprsPacket packet, byte port = 0, byte command = 0)
    {
        packet.Validate();

        var information = Encoding.ASCII.GetBytes(AprsPayloadCodec.Encode(packet.Payload));
        var frame = new Ax25Frame(
            Ax25Codec.ParseAddress(packet.Destination),
            Ax25Codec.ParseAddress(packet.Source),
            packet.Path.Select(Ax25Codec.ParseAddress).ToArray(),
            Ax25Codec.UiFrameControl,
            Ax25Codec.NoLayer3ProtocolId,
            information);

        var ax25Bytes = Ax25Codec.Encode(frame);
        return KissCodec.Encode(new KissFrame(port, command, ax25Bytes));
    }

    public static AprsPacket Decode(ReadOnlySpan<byte> kissFrame)
    {
        var decodedKissFrame = KissCodec.Decode(kissFrame);
        var ax25Frame = Ax25Codec.Decode(decodedKissFrame.Payload);

        if (ax25Frame.Control != Ax25Codec.UiFrameControl)
        {
            throw new InvalidOperationException("AX.25 frame is not a UI frame.");
        }

        if (ax25Frame.ProtocolId != Ax25Codec.NoLayer3ProtocolId)
        {
            throw new InvalidOperationException("AX.25 frame does not carry APRS text.");
        }

        var payload = Encoding.ASCII.GetString(ax25Frame.Information);
        var path = ax25Frame.Digipeaters.Select(Ax25Codec.FormatAddress).ToArray();

        return new AprsPacket
        {
            Source = Ax25Codec.FormatAddress(ax25Frame.Source),
            Destination = Ax25Codec.FormatAddress(ax25Frame.Destination),
            Path = path,
            Payload = AprsPayloadCodec.Decode(payload),
        };
    }
}

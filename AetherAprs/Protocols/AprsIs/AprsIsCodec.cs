// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Protocols.AprsIs;

using AetherAprs.Models;
using AetherAprs.Protocols.Aprs;
using System;
using System.Collections.Generic;

public static class AprsIsCodec
{
    public static string Encode(AprsIsPacket packet)
    {
        packet.Packet.Validate();
        var payload = AprsPayloadCodec.Encode(packet.Packet.Payload);
        var path = packet.Packet.Path.Count == 0 ? string.Empty : $",{string.Join(',', packet.Packet.Path)}";
        var route = packet.Route.Count == 0 ? string.Empty : $",{string.Join(',', packet.Route)}";
        return $"{packet.Packet.Source}>{packet.Packet.Destination}{path}{route}:{payload}";
    }

    public static AprsIsPacket Decode(string packetLine)
    {
        var separatorIndex = packetLine.IndexOf(':');
        if (separatorIndex <= 0)
        {
            throw new InvalidOperationException("APRS-IS packet is missing its payload separator.");
        }

        var header = packetLine[..separatorIndex];
        var payload = packetLine[(separatorIndex + 1)..];
        var sourceSeparator = header.IndexOf('>');
        if (sourceSeparator <= 0)
        {
            throw new InvalidOperationException("APRS-IS packet is missing its source separator.");
        }

        var source = header[..sourceSeparator];
        var route = header[(sourceSeparator + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (route.Length == 0)
        {
            throw new InvalidOperationException("APRS-IS packet is missing its destination.");
        }

        var destination = route[0];
        var path = new List<string>();
        var aprsIsRoute = new List<string>();
        var aprsIsMetadataStarted = false;

        for (var index = 1; index < route.Length; index++)
        {
            var routeElement = route[index];
            if (aprsIsMetadataStarted || routeElement.StartsWith("q", StringComparison.Ordinal))
            {
                aprsIsMetadataStarted = true;
                aprsIsRoute.Add(routeElement);
            }
            else
            {
                path.Add(routeElement);
            }
        }

        return new AprsIsPacket
        {
            Packet = new AprsPacket
            {
                Source = source,
                Destination = destination,
                Path = path,
                Payload = AprsPayloadCodec.Decode(payload),
            },
            Route = aprsIsRoute,
        };
    }
}

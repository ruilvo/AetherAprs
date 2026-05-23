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
    public static string Encode(AprsEntry entry)
    {
        entry.Validate();
        var payload = AprsPayloadCodec.Encode(entry);
        var path = entry.Path.Count == 0 ? string.Empty : $",{string.Join(',', entry.Path)}";
        var aprsIsRoute = entry.AprsIsRoute.Count == 0 ? string.Empty : $",{string.Join(',', entry.AprsIsRoute)}";
        return $"{entry.Source}>{entry.Destination}{path}{aprsIsRoute}:{payload}";
    }

    public static AprsEntry Decode(string packetLine)
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

        var entry = AprsPayloadCodec.Decode(source, destination, path, payload);
        return new AprsEntry
        {
            Kind = entry.Kind,
            Source = entry.Source,
            Destination = entry.Destination,
            Path = entry.Path,
            AprsIsRoute = aprsIsRoute,
            Location = entry.Location,
            Comment = entry.Comment,
            Symbol = entry.Symbol,
            Timestamp = entry.Timestamp,
            AprsTimestamp = entry.AprsTimestamp,
            StationDataType = entry.StationDataType,
            ObjectName = entry.ObjectName,
            ItemName = entry.ItemName,
            MessageRecipient = entry.MessageRecipient,
            MessageText = entry.MessageText,
            MessageId = entry.MessageId,
            IsAlive = entry.IsAlive,
        };
    }
}

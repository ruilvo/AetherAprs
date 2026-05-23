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
        return $"{entry.Source}>{entry.Destination}{path}:{payload}";
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
        var path = route.Length > 1 ? route[1..] : [];
        return AprsPayloadCodec.Decode(source, destination, path, payload);
    }
}

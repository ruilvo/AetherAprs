// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models;

using System;
using System.Collections.Generic;

public sealed class AprsPacket
{
    public required string Source { get; init; }

    public required string Destination { get; init; }

    public IReadOnlyList<string> Path { get; init; } = [];

    public required AprsEntry Payload { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Source))
        {
            throw new InvalidOperationException("Packet source is required.");
        }

        if (string.IsNullOrWhiteSpace(Destination))
        {
            throw new InvalidOperationException("Packet destination is required.");
        }

        Payload.Validate();
    }
}

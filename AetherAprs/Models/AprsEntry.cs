// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models;

using System;
using NetTopologySuite.Geometries;

public enum AprsEntryKind
{
    Station,
    Object,
    Item,
    Message,
}

public sealed class AprsEntry
{
    public required AprsEntryKind Kind { get; init; }

    public required string Source { get; init; }

    public Point? Location { get; init; }

    public required string Message { get; init; }

    public required AprsSymbol Symbol { get; init; }

    public DateTimeOffset? Timestamp { get; init; }
}

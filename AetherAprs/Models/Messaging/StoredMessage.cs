// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using System;

namespace AetherAprs.Models.Messaging;

/// <summary>
/// A stored APRS message belonging to a conversation.
/// </summary>
public sealed class StoredMessage
{
    public required Callsign Peer { get; init; }

    public required string Text { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required bool IsOutbound { get; init; }

    public int? MessageNumber { get; init; }

    public Guid? PortId { get; init; }
}

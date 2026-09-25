// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Represents an APRS station capabilities packet (&lt; format).
/// </summary>
public sealed record CapabilitiesPacket : AprsPacket
{
    /// <summary>
    /// Raw capabilities text.
    /// </summary>
    public string Text { get; init; } = string.Empty;
}

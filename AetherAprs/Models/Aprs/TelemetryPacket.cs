// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Represents an APRS telemetry packet (T#nnn format).
/// </summary>
public sealed record TelemetryPacket : AprsPacket
{
    /// <summary>
    /// Telemetry sequence number.
    /// </summary>
    public int SequenceNumber { get; init; }

    /// <summary>
    /// Analog values (up to 5 channels).
    /// </summary>
    public IReadOnlyList<double> AnalogValues { get; init; } = [];

    /// <summary>
    /// Digital value (8 bits).
    /// </summary>
    public byte? DigitalValue { get; init; }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Modems.Kiss;

/// <summary>
/// Protocol byte constants for the KISS (Keep It Simple, Stupid) framing format.
/// </summary>
public static class KissConstants
{
    /// <summary>
    /// Frame End (0xC0). Marks the start and end of a KISS frame.
    /// </summary>
    public const byte FEND = 0xC0;

    /// <summary>
    /// Frame Escape (0xDB). Indicates the next byte is a transposed value.
    /// </summary>
    public const byte FESC = 0xDB;

    /// <summary>
    /// Transposed FEND (0xDC). Replaces 0xC0 within escaped data.
    /// </summary>
    public const byte TFEND = 0xDC;

    /// <summary>
    /// Transposed FESC (0xDD). Replaces 0xDB within escaped data.
    /// </summary>
    public const byte TFESC = 0xDD;
}
// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Modems.Kiss;

/// <summary>
/// Represents a single decoded KISS frame.
/// </summary>
/// <param name="Command">The command byte. Lower 4 bits = <see cref="KissCommandType"/>, upper 4 bits = port number.</param>
/// <param name="Data">The frame payload (already unescaped). For data frames, this is the AX.25 frame.</param>
public readonly record struct KissFrame(byte Command, byte[] Data)
{
    /// <summary>
    /// Gets the command type from the lower 4 bits of <see cref="Command"/>.
    /// </summary>
    public KissCommandType CommandType => (KissCommandType)(Command & 0x0F);

    /// <summary>
    /// Gets the port number from the upper 4 bits of <see cref="Command"/>.
    /// </summary>
    public int Port => (Command >> 4) & 0x0F;
}
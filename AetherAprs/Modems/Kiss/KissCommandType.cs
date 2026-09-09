// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Modems.Kiss;

/// <summary>
/// KISS command types. These occupy the lower 4 bits of the command byte.
/// The upper 4 bits specify the port number (0-15).
/// </summary>
public enum KissCommandType : byte
{
    /// <summary>
    /// Data frame (0x00). Contains a regular AX.25 frame.
    /// </summary>
    DataFrame = 0x00,

    /// <summary>
    /// Set transmitter key-up delay in 10ms units (0x01).
    /// </summary>
    TXDelay = 0x01,

    /// <summary>
    /// Set persistence (P-persistence CSMA) in range 0-255 (0x02).
    /// </summary>
    Persistence = 0x02,

    /// <summary>
    /// Set slot interval in 10ms units (0x03).
    /// </summary>
    SlotTime = 0x03,

    /// <summary>
    /// Set transmitter tail time in 10ms units (0x04).
    /// </summary>
    TXTail = 0x04,

    /// <summary>
    /// Set full duplex on (non-zero) or off (zero) (0x05).
    /// </summary>
    FullDuplex = 0x05,

    /// <summary>
    /// Set hardware-specific parameters (0x06). Meaning is TNC-dependent.
    /// </summary>
    SetHardware = 0x06,

    /// <summary>
    /// Return command (0xFF). Exits KISS mode and returns TNC to normal operation.
    /// </summary>
    Return = 0xFF,
}
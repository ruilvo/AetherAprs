// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Android.Services.Transports;

/// <summary>
/// Turns an ATT MTU into the number of payload bytes a write can carry. Kept free of
/// Android types so the fallback rules are unit-testable.
/// </summary>
/// <remarks>
/// An ATT write PDU spends three bytes on the opcode and attribute handle, so the
/// usable payload is the MTU minus three. Android's stack truncates a write larger
/// than the payload and still reports success, so the value has to be exact; an MTU
/// that cannot carry a payload at all falls back to the 23-byte baseline that every
/// Bluetooth LE peer supports.
/// </remarks>
internal static class GattPayloadSize
{
    /// <summary>Requested ATT MTU; 517 is the largest ATT PDU a peer can accept.</summary>
    public const int RequestedAttMtu = 517;

    /// <summary>ATT MTU every Bluetooth LE peer supports.</summary>
    public const int DefaultAttMtu = 23;

    /// <summary>ATT write header (opcode plus attribute handle).</summary>
    public const int AttHeaderSize = 3;

    /// <summary>Largest usable payload, with the requested MTU.</summary>
    public const int MaxPayloadSize = RequestedAttMtu - AttHeaderSize;

    /// <summary>Usable payload with the baseline MTU.</summary>
    public const int DefaultPayloadSize = DefaultAttMtu - AttHeaderSize;

    /// <summary>
    /// Converts a reported ATT MTU into a payload size.
    /// </summary>
    /// <param name="attMtu">
    /// MTU reported by the stack. Zero or a negative value means "not reported" and
    /// yields the baseline payload.
    /// </param>
    /// <returns>Usable bytes per write, never below <see cref="DefaultPayloadSize"/>.</returns>
    public static int FromMtu(int attMtu)
    {
        if (attMtu <= AttHeaderSize)
        {
            return DefaultPayloadSize;
        }

        return Math.Min(attMtu - AttHeaderSize, MaxPayloadSize);
    }
}

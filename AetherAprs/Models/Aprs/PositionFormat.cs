// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models.Aprs;

/// <summary>
/// Selects the wire encoding used when transmitting an APRS position report.
/// </summary>
public enum PositionFormat
{
    /// <summary>
    /// Uncompressed position: <c>!DDMM.mmN/DDDMM.mmWC</c> (human readable, longest).
    /// </summary>
    Uncompressed,

    /// <summary>
    /// Compressed position: base-91 encoding of latitude and longitude
    /// (<c>!&lt;table&gt;aaaa bbbb &lt;symbol&gt; csT</c>). Default for mobile use.
    /// </summary>
    Compressed,

    /// <summary>
    /// Mic-E: latitude is encoded in the AX.25 destination address, and
    /// longitude/course/speed are packed into the info field. Compactest format.
    /// </summary>
    MicE,
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Protocols.Ax25;

using System.Collections.Generic;

public sealed record Ax25Frame(
    Ax25Address Destination,
    Ax25Address Source,
    IReadOnlyList<Ax25Address> Digipeaters,
    byte Control,
    byte ProtocolId,
    byte[] Information);

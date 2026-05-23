// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models;

using System.Collections.Generic;

public sealed class AprsIsPacket
{
    public required AprsPacket Packet { get; init; }

    public IReadOnlyList<string> Route { get; init; } = [];
}

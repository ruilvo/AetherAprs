// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Models;

public enum AprsSymbolTable
{
    Primary,
    Secondary,
}

public sealed record AprsSymbol
{
    public required AprsSymbolTable Table { get; init; }

    public required char Code { get; init; }

    public char? Overlay { get; init; }
}

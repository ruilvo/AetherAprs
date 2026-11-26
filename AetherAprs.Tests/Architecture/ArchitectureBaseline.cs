// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Records boundary violations that exist today and are owned by a roadmap work
/// item. The scan fails when a violation is not baselined, so no new one can slip
/// in, and it also fails when a baselined violation disappears, which forces the
/// exemption to be deleted with the migration that fixed it.
/// </summary>
/// <remarks>
/// Every entry names the owning roadmap item so the debt is traceable. Never add
/// an entry to make a new violation pass; fix the violation or re-scope the work.
/// </remarks>
internal static class ArchitectureBaseline
{
    /// <summary>Baselined violation key to the roadmap work item that will remove it.</summary>
    public static IReadOnlyDictionary<string, string> Entries { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // R1.3 moves port persistence behind IPortRegistry.
        ["A001 PortService::AppDbContext"] = "R1.3",
        ["A001 PortService::IDbContextFactory"] = "R1.3",
        ["A001 PortService::SaveChanges"] = "R1.3",

        // R1.3 moves beacon persistence behind IBeaconStateStore.
        ["A001 BeaconService::AppDbContext"] = "R1.3",
        ["A001 BeaconService::IDbContextFactory"] = "R1.3",
        ["A001 BeaconService::SaveChanges"] = "R1.3"
    };
}

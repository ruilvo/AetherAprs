// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Records boundary violations that exist today and are owned by a work item. The
/// scan fails when a violation is not baselined, so no new one can slip in, and it
/// also fails when a baselined violation disappears, which forces the exemption to
/// be deleted with the migration that fixed it.
/// </summary>
/// <remarks>
/// Every entry names the owning work item so the debt is traceable. Never add an
/// entry to make a new violation pass; fix the violation or re-scope the work.
/// </remarks>
internal static class ArchitectureBaseline
{
    /// <summary>Baselined violation key to the work item that will remove it.</summary>
    public static IReadOnlyDictionary<string, string> Entries { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

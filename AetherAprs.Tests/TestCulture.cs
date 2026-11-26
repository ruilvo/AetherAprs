// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace AetherAprs.Tests;

/// <summary>
/// Pins the UI culture for the whole test process before any test runs.
/// </summary>
/// <remarks>
/// Assertions compare localized text, so the process must not pick up the host
/// machine's language. Setting it once here replaces the per-fixture
/// apply-and-restore that used to oscillate between the host culture and English:
/// a fixture restoring the culture it captured could hand a different language to a
/// test that ran in parallel, which made localized assertions fail intermittently.
/// </remarks>
internal static class TestCulture
{
    /// <summary>Applies English for every thread in the test process.</summary>
    [ModuleInitializer]
    internal static void Initialize() => UiCulture.Apply(CultureInfo.GetCultureInfo("en-US"));
}

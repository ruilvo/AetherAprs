// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;

namespace AetherAprs.Services.UI;

/// <summary>
/// Applies the operator's persisted theme choice to the presentation layer so
/// ViewModels never touch framework theme APIs directly.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Applies <paramref name="mode"/>. Implementations must tolerate being called
    /// before the presentation layer exists, for example during startup.
    /// </summary>
    void Apply(ThemeMode mode);
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Configuration;

/// <summary>
/// Selects the application color theme (light, dark, or follow the operating system).
/// </summary>
public enum ThemeMode
{
    /// <summary>
    /// Follow the operating system's current light/dark preference.
    /// </summary>
    System,

    /// <summary>
    /// Always use the light theme.
    /// </summary>
    Light,

    /// <summary>
    /// Always use the dark theme.
    /// </summary>
    Dark
}

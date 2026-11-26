// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using Avalonia;
using Avalonia.Styling;

namespace AetherAprs.Services.UI;

/// <summary>
/// Avalonia-backed <see cref="IThemeService"/>. Setting the requested theme
/// variant makes the MaterialTheme (BaseTheme="Inherit") re-apply its light or
/// dark palette.
/// </summary>
public sealed class AvaloniaThemeService : IThemeService
{
    public static AvaloniaThemeService Instance { get; } = new();

    public void Apply(ThemeMode mode)
    {
        Application? application = Application.Current;
        if (application is null)
        {
            return;
        }

        application.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}

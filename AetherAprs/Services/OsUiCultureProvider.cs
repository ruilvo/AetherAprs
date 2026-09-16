// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;

namespace AetherAprs.Services;

/// <summary>
/// Uses the operating-system UI culture (desktop and other non-Android hosts).
/// </summary>
public sealed class OsUiCultureProvider : IUiCultureProvider
{
    public CultureInfo GetUiCulture()
    {
        return CultureInfo.CurrentUICulture;
    }
}

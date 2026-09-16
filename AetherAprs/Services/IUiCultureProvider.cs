// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;

namespace AetherAprs.Services;

/// <summary>
/// Discovers the UI culture from the host platform.
/// </summary>
public interface IUiCultureProvider
{
    CultureInfo GetUiCulture();
}

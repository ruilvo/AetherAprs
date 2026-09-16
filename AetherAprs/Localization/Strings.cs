// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Resources;

namespace AetherAprs.Localization;

public static class Strings
{
    public static ResourceManager ResourceManager { get; } =
        new("AetherAprs.Localization.Strings", typeof(Strings).Assembly);

    public static string Get(string name, CultureInfo? culture = null)
    {
        return ResourceManager.GetString(name, culture ?? CultureInfo.CurrentUICulture) ?? name;
    }

    public static string Format(string name, params object?[] args)
    {
        return string.Format(CultureInfo.CurrentUICulture, Get(name), args);
    }
}

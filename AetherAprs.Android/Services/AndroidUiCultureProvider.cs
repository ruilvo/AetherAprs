// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;

namespace AetherAprs.Android.Services;

/// <summary>
/// Reads the per-app locale selected in Android system settings.
/// </summary>
public sealed class AndroidUiCultureProvider : AetherAprs.Services.IUiCultureProvider
{
    public CultureInfo GetUiCulture()
    {
        var locale = global::Android.App.Application.Context.Resources?.Configuration?.Locales?.Get(0);
        if (locale is null)
        {
            return CultureInfo.CurrentUICulture;
        }

        var languageTag = locale.ToLanguageTag();
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return CultureInfo.CurrentUICulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(languageTag);
        }
        catch (CultureNotFoundException)
        {
            var language = locale.Language;
            if (string.IsNullOrWhiteSpace(language))
            {
                return CultureInfo.GetCultureInfo("en");
            }

            try
            {
                return CultureInfo.GetCultureInfo(language);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.GetCultureInfo("en");
            }
        }
    }
}

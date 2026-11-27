// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Keeps the Android per-app language picker in step with the languages the
/// application actually ships.
/// </summary>
/// <remarks>
/// Android only offers the locales an application declares in its locale
/// configuration. A translation that resolves at run time is still unreachable for
/// the operator when the picker omits it, so the two lists have to be checked
/// against each other instead of against a hand-maintained note.
/// </remarks>
public sealed class LocaleConfigTests
{
    private static readonly string RepositoryRoot = SourceScanner.FindRepositoryRoot();

    private static readonly string LocaleConfigPath = Path.Combine(
        RepositoryRoot,
        "AetherAprs.Android",
        "Resources",
        "xml",
        "locales_config.xml");

    private static readonly string LocalizationDirectory = Path.Combine(
        RepositoryRoot,
        "AetherAprs",
        "Localization");

    [Fact]
    public void EveryShippedLanguageIsOfferedInThePerAppLanguagePicker()
    {
        // Android declares a full tag ("en-US", "pt-BR") while a resource file names
        // only a culture ("en", "pt-BR"), so the two are matched on the language, not
        // on the tag.
        IReadOnlyList<string> declared =
            [.. ReadDeclaredLocales().Select(LanguageOf)];

        string[] missing =
        [
            .. GetShippedLanguages()
                .Where(language => !declared.Contains(LanguageOf(language)))
                .Order(System.StringComparer.Ordinal)
        ];

        Assert.True(
            missing.Length == 0,
            $"locales_config.xml does not declare: {string.Join(", ", missing)}. "
            + "A language that is not declared cannot be selected in the Android per-app language settings.");
    }

    [Fact]
    public void EveryDeclaredLocaleHasShippedResources()
    {
        IReadOnlyList<string> shipped =
            [.. GetShippedLanguages().Select(LanguageOf)];

        string[] orphaned =
        [
            .. ReadDeclaredLocales()
                .Where(locale => !shipped.Contains(LanguageOf(locale)))
                .Order(System.StringComparer.Ordinal)
        ];

        Assert.True(
            orphaned.Length == 0,
            $"locales_config.xml offers locales with no translated resources: {string.Join(", ", orphaned)}.");
    }

    private static IReadOnlyList<string> ReadDeclaredLocales()
    {
        XDocument document = XDocument.Load(LocaleConfigPath);
        XNamespace android = "http://schemas.android.com/apk/res/android";

        return
        [
            .. document
                .Root!
                .Elements("locale")
                .Select(element => element.Attribute(android + "name")!.Value)
        ];
    }

    /// <summary>Reduces a culture name or an Android locale tag to its language.</summary>
    private static string LanguageOf(string cultureOrLocale)
    {
        int separator = cultureOrLocale.IndexOf('-', System.StringComparison.Ordinal);
        return separator > 0 ? cultureOrLocale[..separator] : cultureOrLocale;
    }

    /// <summary>
    /// Reads the languages whose resources are embedded, keyed by the culture prefix
    /// a locale declaration is matched against.
    /// </summary>
    /// <remarks>
    /// <c>Strings.resx</c> is the neutral English resource and carries no culture in
    /// its name, so English is stated separately rather than derived.
    /// </remarks>
    private static IReadOnlyList<string> GetShippedLanguages()
    {
        List<string> languages = ["en"];

        foreach (string path in Directory.GetFiles(LocalizationDirectory, "Strings.*.resx"))
        {
            // Strings.pt-BR.resx -> pt-BR
            string culture = Path.GetFileNameWithoutExtension(path)["Strings.".Length..];
            languages.Add(culture);

            // A regional variant needs its neutral language to resolve too, so the
            // picker is checked against both.
            int separator = culture.IndexOf('-', System.StringComparison.Ordinal);
            if (separator > 0)
            {
                languages.Add(culture[..separator]);
            }
        }

        return languages;
    }
}

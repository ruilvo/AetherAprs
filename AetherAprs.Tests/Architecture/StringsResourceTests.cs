// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using AetherAprs.Localization;

using Xunit;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Keeps every shipped language file in step with the neutral English resource, so a
/// new key cannot ship in one language only and a translated entry cannot silently drop
/// a format argument.
/// </summary>
public sealed partial class StringsResourceTests
{
    private static readonly string LocalizationDirectory =
        Path.Combine(SourceScanner.FindRepositoryRoot(), "AetherAprs", "Localization");

    private static readonly string NeutralResourcePath =
        Path.Combine(LocalizationDirectory, "Strings.resx");

    [Fact]
    public void LocalizationFolderContainsTheNeutralResourceAndAtLeastOneTranslation()
    {
        Assert.True(File.Exists(NeutralResourcePath), NeutralResourcePath);

        IReadOnlyList<string> translations = GetTranslationResourcePaths();

        Assert.NotEmpty(translations);
    }

    [Fact]
    public void EveryTranslationHasExactlyTheKeysOfTheNeutralResource()
    {
        Dictionary<string, string> neutral = ReadEntries(NeutralResourcePath);

        foreach (string path in GetTranslationResourcePaths())
        {
            Dictionary<string, string> translated = ReadEntries(path);

            string[] missing = [.. neutral.Keys.Except(translated.Keys).Order(StringComparer.Ordinal)];
            string[] extra = [.. translated.Keys.Except(neutral.Keys).Order(StringComparer.Ordinal)];

            Assert.True(
                missing.Length == 0,
                $"{Path.GetFileName(path)} is missing: {string.Join(", ", missing)}");
            Assert.True(
                extra.Length == 0,
                $"{Path.GetFileName(path)} has keys that the neutral resource does not: {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void EveryEntryHasAValueInEveryLanguage()
    {
        foreach (string path in GetResourcePaths())
        {
            foreach ((string key, string value) in ReadEntries(path))
            {
                Assert.True(
                    !string.IsNullOrWhiteSpace(value),
                    $"{Path.GetFileName(path)} has an empty value for {key}");
            }
        }
    }

    [Fact]
    public void EveryTranslationKeepsTheFormatArgumentsOfTheNeutralResource()
    {
        Dictionary<string, string> neutral = ReadEntries(NeutralResourcePath);

        foreach (string path in GetTranslationResourcePaths())
        {
            Dictionary<string, string> translated = ReadEntries(path);

            foreach ((string key, string neutralValue) in neutral)
            {
                string translatedValue = translated[key];

                Assert.True(
                    GetFormatArguments(neutralValue).SetEquals(GetFormatArguments(translatedValue)),
                    $"{Path.GetFileName(path)} changes the format arguments of {key}: "
                        + $"'{neutralValue}' became '{translatedValue}'");
            }
        }
    }

    [Fact]
    public void EveryShippedLanguageResolvesThroughTheResourceManager()
    {
        Dictionary<string, string> neutral = ReadEntries(NeutralResourcePath);
        CultureInfo neutralCulture = CultureInfo.GetCultureInfo("en");

        foreach (string path in GetTranslationResourcePaths())
        {
            string cultureName = Path.GetFileNameWithoutExtension(path)["Strings.".Length..];
            CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);

            int translatedEntries = 0;

            foreach (string key in neutral.Keys)
            {
                string? value = Strings.ResourceManager.GetString(key, culture);

                Assert.False(string.IsNullOrWhiteSpace(value), $"{cultureName} does not resolve {key}.");

                if (!string.Equals(value, Strings.Get(key, neutralCulture), StringComparison.Ordinal))
                {
                    translatedEntries++;
                }
            }

            Assert.True(translatedEntries > 0, $"{cultureName} resources were not embedded; every key fell back to English.");
        }
    }

    private static IReadOnlyList<string> GetResourcePaths() =>
        [NeutralResourcePath, .. GetTranslationResourcePaths()];

    private static IReadOnlyList<string> GetTranslationResourcePaths() =>
        [.. Directory
            .GetFiles(LocalizationDirectory, "Strings.*.resx")
            .Order(StringComparer.Ordinal)];

    private static Dictionary<string, string> ReadEntries(string path)
    {
        Dictionary<string, string> entries = new(StringComparer.Ordinal);

        foreach (XElement data in XDocument.Load(path).Root!.Elements("data"))
        {
            string key = data.Attribute("name")!.Value;

            Assert.True(entries.TryAdd(key, data.Element("value")?.Value ?? string.Empty), $"{path} repeats {key}");
        }

        return entries;
    }

    private static HashSet<string> GetFormatArguments(string value) =>
        [.. FormatArgumentPattern().Matches(value).Select(match => match.Groups[1].Value)];

    [GeneratedRegex(@"\{(\d+)(?::[^}]*)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex FormatArgumentPattern();
}

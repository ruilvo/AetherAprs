// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using AetherAprs.Configuration.Settings.Migrations;
using AetherAprs.Models.Aprs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.Configuration.Settings;

/// <summary>
/// Tests for <see cref="LegacySymbolKeyNormalizer"/>. The normalizer runs on
/// every load because saved files still use the historical JSON property
/// names, so it must accept both the single-character spelling and the already
/// normalized enum-name spelling.
/// </summary>
public sealed class LegacySymbolKeyNormalizerTests
{
    [Theory]
    [InlineData("/", SymbolTable.Primary)]
    [InlineData("\\", SymbolTable.Alternate)]
    public void Normalize_ConvertsSingleCharacterTable(string raw, SymbolTable expected)
    {
        Dictionary<string, string?> values = new()
        { ["Aprs:SymbolTableCharacter"] = raw };

        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);

        Assert.False(values.ContainsKey("Aprs:SymbolTableCharacter"));
        Assert.Equal(expected.ToString(), values["Aprs:SymbolTable"]);
    }

    [Fact]
    public void Normalize_ConvertsDefaultPrefixedTableKey()
    {
        Dictionary<string, string?> values = new()
        {
            ["Aprs:DefaultSymbolTableCharacter"] = "\\",
        };

        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);

        Assert.False(values.ContainsKey("Aprs:DefaultSymbolTableCharacter"));
        Assert.Equal(SymbolTable.Alternate.ToString(), values["Aprs:SymbolTable"]);
    }

    [Fact]
    public void Normalize_AcceptsAlreadyNormalizedTableEnumName()
    {
        Dictionary<string, string?> values = new()
        {
            ["Aprs:SymbolTableCharacter"] = SymbolTable.Alternate.ToString(),
        };

        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);

        Assert.Equal(SymbolTable.Alternate.ToString(), values["Aprs:SymbolTable"]);
    }

    [Fact]
    public void Normalize_DropsInvalidTableValueAndKeepsDefault()
    {
        Dictionary<string, string?> values = new()
        { ["Aprs:SymbolTableCharacter"] = "X" };

        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);

        Assert.False(values.ContainsKey("Aprs:SymbolTableCharacter"));
        Assert.False(values.ContainsKey("Aprs:SymbolTable"));
    }

    [Fact]
    public void Normalize_PrefersExistingTypedValueOverLegacyKey()
    {
        Dictionary<string, string?> values = new()
        {
            ["Aprs:SymbolTable"] = SymbolTable.Primary.ToString(),
            ["Aprs:SymbolTableCharacter"] = "\\",
        };

        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);

        Assert.Equal(SymbolTable.Primary.ToString(), values["Aprs:SymbolTable"]);
        Assert.False(values.ContainsKey("Aprs:SymbolTableCharacter"));
    }

    [Fact]
    public void Normalize_ConvertsSingleCharacterSymbolCode()
    {
        Dictionary<string, string?> values = new()
        { ["Aprs:SymbolCodeCharacter"] = ">" };

        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);

        Assert.False(values.ContainsKey("Aprs:SymbolCodeCharacter"));
        Assert.Equal('>'.ToSymbolCode().ToString(), values["Aprs:SymbolCode"]);
    }

    [Fact]
    public void Normalize_ConvertsDefaultPrefixedSymbolCodeKey()
    {
        Dictionary<string, string?> values = new()
        {
            ["Aprs:DefaultSymbolCodeCharacter"] = "[",
        };

        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);

        Assert.False(values.ContainsKey("Aprs:DefaultSymbolCodeCharacter"));
        Assert.Equal('['.ToSymbolCode().ToString(), values["Aprs:SymbolCode"]);
    }

    [Fact]
    public void Normalize_AcceptsAlreadyNormalizedSymbolCodeEnumName()
    {
        string enumName = SymbolCode.GreaterThanSign.ToString();
        Dictionary<string, string?> values = new()
        { ["Aprs:SymbolCodeCharacter"] = enumName };

        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);

        Assert.Equal(enumName, values["Aprs:SymbolCode"]);
    }

    [Fact]
    public void Normalize_ConvertsOverlayCharacter()
    {
        Dictionary<string, string?> values = new()
        { ["Aprs:SymbolOverlayCharacter"] = "S" };

        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);

        Assert.False(values.ContainsKey("Aprs:SymbolOverlayCharacter"));
        Assert.Equal('S'.ToSymbolCode().ToString(), values["Aprs:SymbolOverlay"]);
    }

    [Fact]
    public void Normalize_DropsMultiCharacterSymbolCode()
    {
        Dictionary<string, string?> values = new()
        { ["Aprs:SymbolCodeCharacter"] = ">>" };

        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);

        Assert.False(values.ContainsKey("Aprs:SymbolCodeCharacter"));
        Assert.False(values.ContainsKey("Aprs:SymbolCode"));
    }

    [Fact]
    public void Normalize_LeavesUnrelatedKeysUntouched()
    {
        Dictionary<string, string?> values = new()
        {
            ["Aprs:Callsign"] = "CT7ALW",
            ["Theme"] = "Dark",
        };

        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);

        Assert.Equal("CT7ALW", values["Aprs:Callsign"]);
        Assert.Equal("Dark", values["Theme"]);
        Assert.Equal(2, values.Count);
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        Dictionary<string, string?> values = new()
        {
            ["Aprs:SymbolTableCharacter"] = "\\",
            ["Aprs:SymbolCodeCharacter"] = ">",
            ["Aprs:SymbolOverlayCharacter"] = "S",
        };

        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);
        Dictionary<string, string?> afterFirst = [with(values)];
        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);

        Assert.Equal(afterFirst.Count, values.Count);
        foreach (KeyValuePair<string, string?> pair in afterFirst)
        {
            Assert.Equal(pair.Value, values[pair.Key]);
        }
    }

    [Fact]
    public void Normalize_HandlesEmptyDictionary()
    {
        Dictionary<string, string?> values = [];

        LegacySymbolKeyNormalizer.Normalize(values, NullLogger.Instance);

        Assert.Empty(values);
    }
}

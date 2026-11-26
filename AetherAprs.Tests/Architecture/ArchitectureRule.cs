// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.Tests.Architecture;

/// <summary>A boundary violation attributable to one type in one source file.</summary>
/// <param name="RuleId">The rule that rejected the file.</param>
/// <param name="File">Repository-relative path of the offending file.</param>
/// <param name="Type">Declaring type, derived from the file name.</param>
/// <param name="Symbol">The forbidden symbol that was found.</param>
internal sealed record ArchitectureViolation(string RuleId, string File, string Type, string Symbol)
{
    /// <summary>The stable key used by the checked-in baseline.</summary>
    public string Key => $"{RuleId} {Type}::{Symbol}";

    public override string ToString() => $"{RuleId} {File} -> {Symbol}";
}

/// <summary>
/// One persistence or presentation ownership rule: the symbols it forbids and the
/// files where that concern legitimately lives.
/// </summary>
internal sealed class ArchitectureRule(
    string id,
    string description,
    IReadOnlyList<string> forbiddenSymbols,
    IReadOnlySet<string> allowedFiles)
{
    public string Id { get; } = id;

    public string Description { get; } = description;

    /// <summary>True when <paramref name="source"/> legitimately owns this concern.</summary>
    public bool IsAllowed(ScannedSource source) => allowedFiles.Contains(source.RelativePath);

    /// <summary>The forbidden symbols present in already-stripped source.</summary>
    public IEnumerable<string> FindViolations(ScannedSource strippedSource) =>
        forbiddenSymbols.Where(symbol => SourceSymbolSearch.Contains(strippedSource.Text, symbol));
}

/// <summary>
/// Whole-token search over C# source, so a rule for <c>Set&lt;</c> does not also
/// match <c>HashSet&lt;</c> and a rule for <c>AppDbContext</c> does not match
/// <c>MyAppDbContextWrapper</c>.
/// </summary>
internal static class SourceSymbolSearch
{
    /// <summary>
    /// True when <paramref name="symbol"/> occurs in <paramref name="text"/> and is
    /// not immediately preceded by an identifier character.
    /// </summary>
    public static bool Contains(string text, string symbol)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            return false;
        }

        var start = 0;
        while (start <= text.Length - symbol.Length)
        {
            var index = text.IndexOf(symbol, start, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            if (index == 0 || !IsIdentifierCharacter(text[index - 1]))
            {
                return true;
            }

            start = index + 1;
        }

        return false;
    }

    private static bool IsIdentifierCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';
}

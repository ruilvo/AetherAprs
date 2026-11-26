// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Text;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Removes comments and string literal contents from C# source so architecture
/// rules match executable code only.
/// </summary>
/// <remarks>
/// Documentation that mentions a symbol is not a dependency on it, and a message
/// string that happens to contain a type name is not a boundary violation. Each
/// removed span is replaced by a single space so surrounding tokens stay
/// separated and reported offsets remain stable.
/// </remarks>
internal static class SourceTextStripper
{
    /// <summary>Returns <paramref name="source"/> with comments and literal contents blanked.</summary>
    public static string StripCommentsAndLiterals(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        StringBuilder result = new(source.Length);
        int index = 0;
        while (index < source.Length)
        {
            char current = source[index];
            char next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (current == '/' && next == '/')
            {
                index = SkipLineComment(source, index);
                result.Append(' ');
                continue;
            }

            if (current == '/' && next == '*')
            {
                index = SkipBlockComment(source, index);
                result.Append(' ');
                continue;
            }

            if (current == '"')
            {
                index = SkipQuoted(source, index, '"');
                result.Append(' ');
                continue;
            }

            if (current == '\'')
            {
                index = SkipQuoted(source, index, '\'');
                result.Append(' ');
                continue;
            }

            result.Append(current);
            index++;
        }

        return result.ToString();
    }

    private static int SkipLineComment(string source, int index)
    {
        while (index < source.Length && source[index] is not ('\n' or '\r'))
        {
            index++;
        }

        return index;
    }

    private static int SkipBlockComment(string source, int index)
    {
        index += 2;
        while (index < source.Length - 1)
        {
            if (source[index] == '*' && source[index + 1] == '/')
            {
                return index + 2;
            }

            index++;
        }

        return source.Length;
    }

    /// <summary>
    /// Skips a single or interpolated string, verbatim string or character literal.
    /// Assumes <paramref name="index"/> is on the opening delimiter.
    /// </summary>
    private static int SkipQuoted(string source, int index, char delimiter)
    {
        // Verbatim ("@") and raw (""" ... """) strings do not use backslash escapes.
        bool verbatim = index > 0 && source[index - 1] == '@';
        index++;
        while (index < source.Length)
        {
            char current = source[index];
            if (!verbatim && current == '\\' && delimiter != '\'')
            {
                index += 2;
                continue;
            }

            if (verbatim && current == delimiter && index + 1 < source.Length && source[index + 1] == delimiter)
            {
                index += 2;
                continue;
            }

            if (current == delimiter)
            {
                return index + 1;
            }

            index++;
        }

        return source.Length;
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Models.Aprs;

/// <summary>
/// APRS symbol code character (printable ASCII 0x20-0x7E).
/// Maps to the 94 APRS symbol codes used in position reports.
/// </summary>
public enum SymbolCode : byte
{
    /// <summary>ASCII character ' ' (space).</summary>
    Space = 0x20,

    /// <summary>ASCII character '!' (!).</summary>
    Exclamation = 0x21,

    /// <summary>ASCII character '"' (").</summary>
    QuotationMark = 0x22,

    /// <summary>ASCII character '#' (#).</summary>
    NumberSign = 0x23,

    /// <summary>ASCII character '$' ($).</summary>
    DollarSign = 0x24,

    /// <summary>ASCII character '%' (%).</summary>
    PercentSign = 0x25,

    /// <summary>ASCII character '&amp;' (&amp;).</summary>
    Ampersand = 0x26,

    /// <summary>ASCII character ''' (apostrophe).</summary>
    Apostrophe = 0x27,

    /// <summary>ASCII character '(' (().</summary>
    LeftParenthesis = 0x28,

    /// <summary>ASCII character ')' ()).</summary>
    RightParenthesis = 0x29,

    /// <summary>ASCII character '*' (*).</summary>
    Asterisk = 0x2A,

    /// <summary>ASCII character '+' (+).</summary>
    PlusSign = 0x2B,

    /// <summary>ASCII character ',' (,).</summary>
    Comma = 0x2C,

    /// <summary>ASCII character '-' (-).</summary>
    HyphenMinus = 0x2D,

    /// <summary>ASCII character '.' (.).</summary>
    FullStop = 0x2E,

    /// <summary>ASCII character '/' (/).</summary>
    Solidus = 0x2F,

    /// <summary>ASCII character '0' (0).</summary>
    Digit0 = 0x30,

    /// <summary>ASCII character '1' (1).</summary>
    Digit1 = 0x31,

    /// <summary>ASCII character '2' (2).</summary>
    Digit2 = 0x32,

    /// <summary>ASCII character '3' (3).</summary>
    Digit3 = 0x33,

    /// <summary>ASCII character '4' (4).</summary>
    Digit4 = 0x34,

    /// <summary>ASCII character '5' (5).</summary>
    Digit5 = 0x35,

    /// <summary>ASCII character '6' (6).</summary>
    Digit6 = 0x36,

    /// <summary>ASCII character '7' (7).</summary>
    Digit7 = 0x37,

    /// <summary>ASCII character '8' (8).</summary>
    Digit8 = 0x38,

    /// <summary>ASCII character '9' (9).</summary>
    Digit9 = 0x39,

    /// <summary>ASCII character ':' (:).</summary>
    Colon = 0x3A,

    /// <summary>ASCII character ';' (;).</summary>
    Semicolon = 0x3B,

    /// <summary>ASCII character '&lt;' (&lt;).</summary>
    LessThanSign = 0x3C,

    /// <summary>ASCII character '=' (=).</summary>
    EqualsSign = 0x3D,

    /// <summary>ASCII character '&gt;' (&gt;).</summary>
    GreaterThanSign = 0x3E,

    /// <summary>ASCII character '?' (?).</summary>
    QuestionMark = 0x3F,

    /// <summary>ASCII character '@' (@).</summary>
    CommercialAt = 0x40,

    /// <summary>ASCII character 'A' (A).</summary>
    LatinCapitalLetterA = 0x41,

    /// <summary>ASCII character 'B' (B).</summary>
    LatinCapitalLetterB = 0x42,

    /// <summary>ASCII character 'C' (C).</summary>
    LatinCapitalLetterC = 0x43,

    /// <summary>ASCII character 'D' (D).</summary>
    LatinCapitalLetterD = 0x44,

    /// <summary>ASCII character 'E' (E).</summary>
    LatinCapitalLetterE = 0x45,

    /// <summary>ASCII character 'F' (F).</summary>
    LatinCapitalLetterF = 0x46,

    /// <summary>ASCII character 'G' (G).</summary>
    LatinCapitalLetterG = 0x47,

    /// <summary>ASCII character 'H' (H).</summary>
    LatinCapitalLetterH = 0x48,

    /// <summary>ASCII character 'I' (I).</summary>
    LatinCapitalLetterI = 0x49,

    /// <summary>ASCII character 'J' (J).</summary>
    LatinCapitalLetterJ = 0x4A,

    /// <summary>ASCII character 'K' (K).</summary>
    LatinCapitalLetterK = 0x4B,

    /// <summary>ASCII character 'L' (L).</summary>
    LatinCapitalLetterL = 0x4C,

    /// <summary>ASCII character 'M' (M).</summary>
    LatinCapitalLetterM = 0x4D,

    /// <summary>ASCII character 'N' (N).</summary>
    LatinCapitalLetterN = 0x4E,

    /// <summary>ASCII character 'O' (O).</summary>
    LatinCapitalLetterO = 0x4F,

    /// <summary>ASCII character 'P' (P).</summary>
    LatinCapitalLetterP = 0x50,

    /// <summary>ASCII character 'Q' (Q).</summary>
    LatinCapitalLetterQ = 0x51,

    /// <summary>ASCII character 'R' (R).</summary>
    LatinCapitalLetterR = 0x52,

    /// <summary>ASCII character 'S' (S).</summary>
    LatinCapitalLetterS = 0x53,

    /// <summary>ASCII character 'T' (T).</summary>
    LatinCapitalLetterT = 0x54,

    /// <summary>ASCII character 'U' (U).</summary>
    LatinCapitalLetterU = 0x55,

    /// <summary>ASCII character 'V' (V).</summary>
    LatinCapitalLetterV = 0x56,

    /// <summary>ASCII character 'W' (W).</summary>
    LatinCapitalLetterW = 0x57,

    /// <summary>ASCII character 'X' (X).</summary>
    LatinCapitalLetterX = 0x58,

    /// <summary>ASCII character 'Y' (Y).</summary>
    LatinCapitalLetterY = 0x59,

    /// <summary>ASCII character 'Z' (Z).</summary>
    LatinCapitalLetterZ = 0x5A,

    /// <summary>ASCII character '[' ([).</summary>
    LeftSquareBracket = 0x5B,

    /// <summary>ASCII character '\' (backslash).</summary>
    ReverseSolidus = 0x5C,

    /// <summary>ASCII character ']' (]).</summary>
    RightSquareBracket = 0x5D,

    /// <summary>ASCII character '^' (^).</summary>
    CircumflexAccent = 0x5E,

    /// <summary>ASCII character '_' (_).</summary>
    LowLine = 0x5F,

    /// <summary>ASCII character '`' (`).</summary>
    GraveAccent = 0x60,

    /// <summary>ASCII character 'a' (a).</summary>
    LatinSmallLetterA = 0x61,

    /// <summary>ASCII character 'b' (b).</summary>
    LatinSmallLetterB = 0x62,

    /// <summary>ASCII character 'c' (c).</summary>
    LatinSmallLetterC = 0x63,

    /// <summary>ASCII character 'd' (d).</summary>
    LatinSmallLetterD = 0x64,

    /// <summary>ASCII character 'e' (e).</summary>
    LatinSmallLetterE = 0x65,

    /// <summary>ASCII character 'f' (f).</summary>
    LatinSmallLetterF = 0x66,

    /// <summary>ASCII character 'g' (g).</summary>
    LatinSmallLetterG = 0x67,

    /// <summary>ASCII character 'h' (h).</summary>
    LatinSmallLetterH = 0x68,

    /// <summary>ASCII character 'i' (i).</summary>
    LatinSmallLetterI = 0x69,

    /// <summary>ASCII character 'j' (j).</summary>
    LatinSmallLetterJ = 0x6A,

    /// <summary>ASCII character 'k' (k).</summary>
    LatinSmallLetterK = 0x6B,

    /// <summary>ASCII character 'l' (l).</summary>
    LatinSmallLetterL = 0x6C,

    /// <summary>ASCII character 'm' (m).</summary>
    LatinSmallLetterM = 0x6D,

    /// <summary>ASCII character 'n' (n).</summary>
    LatinSmallLetterN = 0x6E,

    /// <summary>ASCII character 'o' (o).</summary>
    LatinSmallLetterO = 0x6F,

    /// <summary>ASCII character 'p' (p).</summary>
    LatinSmallLetterP = 0x70,

    /// <summary>ASCII character 'q' (q).</summary>
    LatinSmallLetterQ = 0x71,

    /// <summary>ASCII character 'r' (r).</summary>
    LatinSmallLetterR = 0x72,

    /// <summary>ASCII character 's' (s).</summary>
    LatinSmallLetterS = 0x73,

    /// <summary>ASCII character 't' (t).</summary>
    LatinSmallLetterT = 0x74,

    /// <summary>ASCII character 'u' (u).</summary>
    LatinSmallLetterU = 0x75,

    /// <summary>ASCII character 'v' (v).</summary>
    LatinSmallLetterV = 0x76,

    /// <summary>ASCII character 'w' (w).</summary>
    LatinSmallLetterW = 0x77,

    /// <summary>ASCII character 'x' (x).</summary>
    LatinSmallLetterX = 0x78,

    /// <summary>ASCII character 'y' (y).</summary>
    LatinSmallLetterY = 0x79,

    /// <summary>ASCII character 'z' (z).</summary>
    LatinSmallLetterZ = 0x7A,

    /// <summary>ASCII character '{' ({).</summary>
    LeftCurlyBracket = 0x7B,

    /// <summary>ASCII character '|' (|).</summary>
    VerticalLine = 0x7C,

    /// <summary>ASCII character '}' (}).</summary>
    RightCurlyBracket = 0x7D,

    /// <summary>ASCII character '~' (~).</summary>
    Tilde = 0x7E,
}

/// <summary>
/// Extension methods for <see cref="SymbolCode"/>.
/// </summary>
public static class SymbolCodeExtensions
{
    /// <summary>
    /// Converts a <see cref="SymbolCode"/> to its ASCII character representation.
    /// </summary>
    public static char ToChar(this SymbolCode code) => (char)code;

    /// <summary>
    /// Converts a printable ASCII character to a <see cref="SymbolCode"/>.
    /// </summary>
    /// <param name="c">The character (0x20-0x7E).</param>
    /// <returns>The corresponding symbol code.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the character is outside the printable ASCII range.</exception>
    public static SymbolCode ToSymbolCode(this char c)
    {
        if (c is < ' ' or > '~')
        {
            throw new ArgumentOutOfRangeException(nameof(c), $"Symbol code must be a printable ASCII character (0x20-0x7E), got '\\x{(int)c:X4}'");
        }

        return (SymbolCode)c;
    }
}
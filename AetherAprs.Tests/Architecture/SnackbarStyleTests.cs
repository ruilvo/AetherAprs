// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;

using Xunit;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Guards the snackbar presentation. Material.Avalonia 3.22 defaults
/// <c>SnackbarHorizontalAlignment</c> to <c>Left</c> and its supporting-text theme
/// hard-codes <c>TextAlignment="Left"</c>, so without an explicit override the toast
/// sits at the left edge with left-aligned text. These assertions read the shipped
/// style file, because the test project has no Avalonia headless host.
/// </summary>
public sealed class SnackbarStyleTests
{
    private static readonly string StylesRoot = Path.Combine(
        FindRepositoryRoot(), "AetherAprs", "Styles");

    [Fact]
    public void TheSnackbarStyleIsIncludedByTheApplication()
    {
        string app = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "AetherAprs", "App.axaml"));

        Assert.Contains("/Styles/SnackbarStyles.axaml", app, StringComparison.Ordinal);
    }

    [Fact]
    public void SnackbarsAreHorizontallyCentred()
    {
        string style = ReadSnackbarStyles();

        Assert.Contains(
            "<Setter Property=\"SnackbarHorizontalAlignment\" Value=\"Center\" />",
            style,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheSnackbarTextIsCentredAndWrapsInsteadOfTrimming()
    {
        string style = ReadSnackbarStyles();
        int textStyle = style.IndexOf("PART_SnackbarSupportingText", StringComparison.Ordinal);

        Assert.True(textStyle >= 0, "The snackbar text style must target PART_SnackbarSupportingText.");

        string block = style[textStyle..];
        Assert.Contains("Property=\"TextAlignment\" Value=\"Center\"", block, StringComparison.Ordinal);
        Assert.Contains("Property=\"TextWrapping\" Value=\"Wrap\"", block, StringComparison.Ordinal);
        Assert.Contains("Property=\"TextTrimming\" Value=\"None\"", block, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSnackbarCardIsCappedToTheViewport()
    {
        string style = ReadSnackbarStyles();

        // Material's 344px minimum width plus margins can exceed a narrow phone.
        Assert.Contains("Property=\"MaxWidth\"", style, StringComparison.Ordinal);
    }

    private static string ReadSnackbarStyles() =>
        File.ReadAllText(Path.Combine(StylesRoot, "SnackbarStyles.axaml"));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AetherAprs.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AetherAprs.Localization;
using AetherAprs.ViewModels;
using AetherAprs.ViewModels.Components;
using Avalonia.Controls;
using Xunit;

namespace AetherAprs.Tests.Architecture;

public sealed partial class ViewLocatorRoutingTests
{
    [Fact]
    public void EveryMappedViewModelName_IsAViewModelType()
    {
        IReadOnlyList<string> mapped = MappedViewModelNames();

        string[] missing = [.. mapped.Where(name => FindViewModelType(name) is null)];

        Assert.NotEmpty(mapped);
        Assert.Empty(missing);
    }

    [Fact]
    public void Build_ForAnUnmappedViewModel_ShowsLocalizedFallbackText()
    {
        Control? view = new AetherAprs.ViewLocator().Build(new TemplateFieldViewModel("summit"));

        TextBlock text = Assert.IsType<TextBlock>(view);
        Assert.Equal(
            Strings.Format("ViewLocatorMissingView", nameof(TemplateFieldViewModel)),
            text.Text);
    }

    [Fact]
    public void Build_ForNoData_ReturnsNothing()
    {
        ViewLocator locator = new();

        Assert.Null(locator.Build(null));
        Assert.False(locator.Match(null));
    }

    /// <summary>
    /// Reads the routing table out of the locator itself, so the test fails if an
    /// entry is renamed, deleted or left behind by a moved view model.
    /// </summary>
    private static IReadOnlyList<string> MappedViewModelNames()
    {
        ScannedSource locator = SourceScanner.ReadProductionSources()
            .Single(source => source.RelativePath.EndsWith("ViewLocator.cs", StringComparison.Ordinal));

        return [.. MyRegex.Matches(locator.Text)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)];
    }

    private static Type? FindViewModelType(string name) =>
        typeof(ViewModelBase).Assembly.GetTypes()
            .FirstOrDefault(type =>
                string.Equals(type.Name, name, StringComparison.Ordinal) &&
                typeof(ViewModelBase).IsAssignableFrom(type));

    [GeneratedRegex(@"(?<name>\w+ViewModel)\s*=>\s*new\s")]
    private static partial Regex MyRegex { get; }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Pins the dependency direction the planned assembly split relies on: domain,
/// configuration, persistence and service code name neither presentation types, the
/// Avalonia framework nor <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>,
/// so those layers can move into their own assembly without revisiting behaviour.
/// Presentation adapters under <c>Services/UI</c> are the one place allowed to
/// translate application state into UI types.
/// </summary>
public sealed class DependencyDirectionTests
{
    private static readonly string[] ApplicationLayerRoots =
    [
        "AetherAprs/Configuration/",
        "AetherAprs/Data/",
        "AetherAprs/Models/",
        "AetherAprs/Services/",
    ];

    private static readonly string[] PresentationAdapterRoots =
    [
        "AetherAprs/Services/UI/",
    ];

    private static readonly string[] PresentationSymbols =
    [
        "using Avalonia",
        "Avalonia.",
        "AetherAprs.ViewModels",
        "AetherAprs.Views",
        "ObservableCollection",
    ];

    [Theory]
    [InlineData("AetherAprs/Models/Aprs/Packet.cs", "using AetherAprs.ViewModels;")]
    [InlineData("AetherAprs/Services/Messaging/MessageService.cs", "AetherAprs.Views.Pages.SettingsView view;")]
    [InlineData("AetherAprs/Data/SqliteRuntimeWriter.cs", "using Avalonia.Threading;")]
    [InlineData("AetherAprs/Services/Transmission/BeaconTransmissionService.cs", "Avalonia.Application.Current;")]
    [InlineData("AetherAprs/Models/Messaging/ConversationThread.cs", "public ObservableCollection<StoredMessage> Messages { get; } = [];")]
    public void ApplicationLayer_NamesPresentationOrAvalonia_IsRejected(string path, string offendingLine) =>
        Assert.False(FindPresentationDependencies(new ScannedSource(path, offendingLine)).Count == 0);

    [Fact]
    public void ApplicationLayer_CommentMentioningAvalonia_IsAllowed() =>
        Assert.True(FindPresentationDependencies(new ScannedSource(
            "AetherAprs/Data/SqliteRuntimeWriter.cs",
            "// Avalonia's synchronous disposal path must also drain admitted work.")).Count == 0);

    [Fact]
    public void PresentationAdapter_MayNameViewModels() =>
        Assert.True(FindPresentationDependencies(new ScannedSource(
            "AetherAprs/Services/UI/NavigationService.cs",
            "using AetherAprs.ViewModels;")).Count == 0);

    [Fact]
    public void PresentationLayer_NamesAvalonia() =>
        Assert.True(FindPresentationDependencies(new ScannedSource(
            "AetherAprs/Views/Pages/SettingsView.axaml.cs",
            "using Avalonia.Controls;")).Count == 0);

    [Fact]
    public void ProductionSource_ApplicationLayerDependsOnNoPresentationType()
    {
        List<string> violations =
        [
            .. from source in SourceScanner.ReadProductionSources()
               from symbol in FindPresentationDependencies(source)
               select $"{source.RelativePath} -> {symbol}"
        ];

        Assert.True(
            violations.Count == 0,
            "Application and service code must not depend on presentation types, Avalonia or "
            + "ObservableCollection. Move the adapter under AetherAprs/Services/UI/."
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static IReadOnlyList<string> FindPresentationDependencies(ScannedSource source)
    {
        if (!IsApplicationLayer(source.RelativePath))
        {
            return [];
        }

        string text = SourceTextStripper.StripCommentsAndLiterals(source.Text);
        return [.. PresentationSymbols.Where(symbol => SourceSymbolSearch.Contains(text, symbol))];
    }

    private static bool IsApplicationLayer(string relativePath)
    {
        bool inApplicationLayer = ApplicationLayerRoots.Any(root =>
            relativePath.StartsWith(root, StringComparison.Ordinal));
        bool inPresentationAdapter = PresentationAdapterRoots.Any(root =>
            relativePath.StartsWith(root, StringComparison.Ordinal));
        return inApplicationLayer && !inPresentationAdapter;
    }
}

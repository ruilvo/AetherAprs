// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AetherAprs.Tests.Architecture;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void ProductionSource_IntroducesNoUnbaselinedBoundaryViolation()
    {
        var violations = ArchitectureBoundaryScanner.Scan();
        var unbaselined = violations
            .Where(violation => !ArchitectureBaseline.Entries.ContainsKey(violation.Key))
            .Select(violation => violation.ToString())
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

        Assert.True(unbaselined.Length == 0,
            "New architecture boundary violations were found. Fix them or, if this is a" + Environment.NewLine +
            "newly discovered pre-existing violation, add it to ArchitectureBaseline with an" + Environment.NewLine +
            "owning roadmap work item:" + Environment.NewLine + string.Join(Environment.NewLine, unbaselined));
    }

    [Fact]
    public void Baseline_ContainsNoStaleEntry()
    {
        var found = ArchitectureBoundaryScanner.Scan().Select(violation => violation.Key).ToHashSet(StringComparer.Ordinal);
        var stale = ArchitectureBaseline.Entries.Keys
            .Where(key => !found.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.True(stale.Length == 0,
            "These baselined violations no longer exist. Delete the baseline entry in the same" + Environment.NewLine +
            "change that removed the violation:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void ProductionSource_ContainsNoDebugWriteLine()
    {
        Assert.Empty(ArchitectureBoundaryScanner.Scan()
            .Where(violation => violation.RuleId == "A003")
            .Select(violation => violation.ToString()));
    }

    public static TheoryData<string, string> PersistenceViolations => new()
    {
        { "AetherAprs/Services/Ports/PortService.cs", "IDbContextFactory<AppDbContext> factory = null!;" },
        { "AetherAprs/Services/Messaging/MessageService.cs", "await db.SaveChangesAsync(token);" },
        { "AetherAprs/ViewModels/Pages/MessagesViewModel.cs", "var set = context.Set<PacketRecord>();" }
    };

    [Theory]
    [MemberData(nameof(PersistenceViolations))]
    public void Scan_SyntheticPersistenceInFeatureCode_IsRejected(string path, string offendingLine)
    {
        var violations = ArchitectureBoundaryScanner.Scan([Source(path, offendingLine)]);

        Assert.Contains(violations, violation => violation.RuleId == "A001");
    }

    [Fact]
    public void Scan_SyntheticUiDispatchInFeatureCode_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs/Services/Messaging/MessageService.cs", "Dispatcher.UIThread.Post(action);")]);

        Assert.Contains(violations, violation => violation.RuleId == "A002");
    }

    [Fact]
    public void Scan_SyntheticDebugWriteLine_IsRejected()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs/Services/Ports/PortService.cs", "Debug.WriteLine(\"x\");")]);

        Assert.Contains(violations, violation => violation.RuleId == "A003");
    }

    [Fact]
    public void Scan_ApprovedOwnerFile_IsNotReported()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs/Data/Packets/PacketStorageService.cs", "await context.SaveChangesAsync(token);")]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_SymbolOnlyInCommentOrString_IsNotReported()
    {
        var violations = ArchitectureBoundaryScanner.Scan(
            [
                Source("AetherAprs/Services/Messaging/MessageService.cs",
                    """
                    // This used to call SaveChanges() through AppDbContext.
                    var message = "AppDbContext SaveChanges Dispatcher.UIThread";
                    /* IDbContextFactory is not used here either. */
                    """)
            ]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Scan_AndroidProjectIsIncluded()
    {
        var sources = SourceScanner.ReadProductionSources();
        Assert.Contains(sources, source => source.Project == "AetherAprs.Android");

        var violations = ArchitectureBoundaryScanner.Scan(
            [Source("AetherAprs.Android/MainActivity.cs", "var factory = provider.GetService<IDbContextFactory<AppDbContext>>();")]);

        Assert.Contains(violations, violation => violation.RuleId == "A001");
    }

    [Fact]
    public void Rules_EachDeclareIdDescriptionAndSymbols()
    {
        Assert.NotEmpty(ArchitectureBoundaryScanner.Rules);
        foreach (var rule in ArchitectureBoundaryScanner.Rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.False(string.IsNullOrWhiteSpace(rule.Description));
        }

        Assert.Equal(
            ArchitectureBoundaryScanner.Rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count(),
            ArchitectureBoundaryScanner.Rules.Count);
    }

    private static ScannedSource Source(string path, string body) =>
        new(path, "namespace Example;" + Environment.NewLine + body);
}

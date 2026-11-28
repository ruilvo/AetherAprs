// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Xunit;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Guards the supply-chain contract of the workflows: every external action is pinned
/// to the commit behind a recorded release, so moving a tag cannot change what runs.
/// </summary>
public sealed class WorkflowPinningTests
{
    private static readonly Regex UsesLine = new(
        @"^\s*(?:-\s+)?uses:\s*(?<reference>\S+)(?<comment>\s*#.*)?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex PinnedReference = new(
        @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex RecordedRelease = new(
        @"^#\s*v\d+\.\d+\.\d+\s*$",
        RegexOptions.CultureInvariant);

    [Fact]
    public void EveryExternalActionIsPinnedToACommit()
    {
        foreach (string path in GetWorkflowPaths())
        {
            foreach (Match match in UsesLine.Matches(File.ReadAllText(path)))
            {
                string reference = match.Groups["reference"].Value;
                Assert.True(
                    PinnedReference.IsMatch(reference),
                    $"{Path.GetFileName(path)} uses '{reference}', which is not pinned to a 40-character commit SHA.");
            }
        }
    }

    [Fact]
    public void EveryPinnedActionRecordsTheReleaseItCameFrom()
    {
        foreach (string path in GetWorkflowPaths())
        {
            foreach (Match match in UsesLine.Matches(File.ReadAllText(path)))
            {
                string comment = match.Groups["comment"].Value.Trim();
                Assert.True(
                    RecordedRelease.IsMatch(comment),
                    $"{Path.GetFileName(path)} pins '{match.Groups["reference"].Value}' without a '# vX.Y.Z' comment, "
                    + "so nobody can tell which release the commit belongs to.");
            }
        }
    }

    [Fact]
    public void TheWorkflowsDoNotTrackMovingMajorVersionTags()
    {
        foreach (string path in GetWorkflowPaths())
        {
            string workflow = File.ReadAllText(path);
            Assert.False(
                Regex.IsMatch(workflow, @"uses:\s*\S+@v\d+\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant),
                $"{Path.GetFileName(path)} tracks a moving major version tag instead of a pinned commit.");
        }
    }

    private static IReadOnlyList<string> GetWorkflowPaths()
    {
        string directory = Path.Combine(SourceScanner.FindRepositoryRoot(), ".github", "workflows");
        Assert.True(Directory.Exists(directory), $"The workflow directory is missing at {directory}.");

        List<string> paths = [.. Directory.GetFiles(directory, "*.yml")];
        paths.Sort(StringComparer.Ordinal);
        Assert.NotEmpty(paths);
        return paths;
    }
}

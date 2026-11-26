// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AetherAprs.Tests.Architecture;

/// <summary>One production source file plus its repository-relative path.</summary>
internal sealed record ScannedSource(string RelativePath, string Text)
{
    /// <summary>
    /// The repository-relative path of the assembly this file belongs to, used by
    /// rules that only apply to the core application.
    /// </summary>
    public string Project => RelativePath.Split('/', 2)[0];

    /// <summary>
    /// The path below the project directory, always using forward slashes so the
    /// rules and the baseline file do not depend on the host platform.
    /// </summary>
    public string ProjectPath => RelativePath.Split('/', 2) is [_, var rest] ? rest : string.Empty;

    /// <summary>
    /// The declaring type the file is expected to hold. The repository convention
    /// is one primary type per file with a namespace that matches its folder, so
    /// the file name identifies the type for the baseline key.
    /// </summary>
    public string TypeName => Path.GetFileNameWithoutExtension(RelativePath);

    /// <summary>The stable baseline key for a violation in this file.</summary>
    public string BaselineKey(string symbol) => $"{TypeName}::{symbol}";
}

/// <summary>
/// Reads the production source tree so architecture rules can be asserted against
/// the code that actually ships, without compiling a separate analyzer assembly.
/// </summary>
internal static class SourceScanner
{
    private static readonly string[] ScannedProjects = ["AetherAprs", "AetherAprs.Android"];

    private static readonly string[] ExcludedSegments = ["/obj/", "/bin/", "/Data/Migrations/"];

    /// <summary>Locates the repository root by walking up for the solution file.</summary>
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AetherAprs.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root above " + AppContext.BaseDirectory);
    }

    /// <summary>Enumerates every production C# file in the scanned projects.</summary>
    public static IReadOnlyList<ScannedSource> ReadProductionSources() =>
        ReadProductionSources(FindRepositoryRoot());

    public static IReadOnlyList<ScannedSource> ReadProductionSources(string repositoryRoot)
    {
        var sources = new List<ScannedSource>();
        foreach (var project in ScannedProjects)
        {
            var directory = Path.Combine(repositoryRoot, project);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
                if (ExcludedSegments.Any(segment => $"/{relativePath}".Contains(segment, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                sources.Add(new ScannedSource(relativePath, File.ReadAllText(file)));
            }
        }

        return sources;
    }
}

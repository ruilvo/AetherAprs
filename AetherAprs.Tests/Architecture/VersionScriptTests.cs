// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Xunit;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Runs the release version script for real: the Android version code packs four
/// decimal fields into one integer, so a tag whose components overflow their field
/// would silently collide with another tag and no device would ever accept the
/// later release as an upgrade.
/// </summary>
public sealed class VersionScriptTests
{
    private const string ScriptRelativePath = ".github/scripts/extract-version.sh";

    [Fact]
    public void FinalReleaseTagProducesItsPackedVersionCode()
    {
        VersionResult result = RunScript("v1.2.3");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1020399", result.Values["version_code"]);
        Assert.Equal("1.2.3", result.Values["version_name"]);
    }

    [Fact]
    public void PrereleaseTagMapsItsSequenceIntoTheBandOfItsType()
    {
        VersionResult result = RunScript("v1.2.3-beta.5");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1020338", result.Values["version_code"]);
        Assert.Equal("1.2.3-beta.5", result.Values["version_name"]);
    }

    [Theory]
    [InlineData("v1.100.0")]
    [InlineData("v1.2.100")]
    [InlineData("v01.2.3")]
    [InlineData("v1.02.3")]
    [InlineData("v2100.0.0")]
    [InlineData("v1.2")]
    [InlineData("v1.2.3-rc.33")]
    public void TagsThatWouldOverflowOrDuplicateAVersionCodeAreRejected(string tag)
    {
        VersionResult result = RunScript(tag);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("version_code=", result.Output, StringComparison.Ordinal);
        Assert.False(
            string.IsNullOrWhiteSpace(result.Error),
            "A rejected tag must explain itself on stderr.");
    }

    [Fact]
    public void EveryTagInTheSupportedRangeGetsADistinctVersionCode()
    {
        Dictionary<string, string> codesByTag = new(StringComparer.Ordinal);

        foreach (int major in new[] { 0, 1, 2 })
        {
            foreach (int minor in new[] { 0, 1, 99 })
            {
                foreach (int patch in new[] { 0, 1, 99 })
                {
                    string tag = FormattableString.Invariant($"v{major}.{minor}.{patch}");
                    VersionResult result = RunScript(tag);
                    Assert.Equal(0, result.ExitCode);

                    string code = result.Values["version_code"];
                    Assert.False(
                        codesByTag.TryGetValue(code, out string? existing),
                        $"{tag} and {existing} both map to version code {code}.");
                    codesByTag.Add(code, tag);
                }
            }
        }
    }

    private static VersionResult RunScript(string tag)
    {
        if (!BashIsAvailable())
        {
            Assert.Skip("bash is not available on this machine, so the release version script cannot be run.");
        }

        string repositoryRoot = SourceScanner.FindRepositoryRoot();
        Assert.True(
            File.Exists(Path.Combine(repositoryRoot, ScriptRelativePath.Replace('/', Path.DirectorySeparatorChar))),
            $"The release version script is missing at {ScriptRelativePath}.");

        ProcessStartInfo startInfo = new("bash")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(ScriptRelativePath);
        startInfo.ArgumentList.Add(tag);

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
            {
                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        return new VersionResult(process.ExitCode, output, error, values);
    }

    private static bool BashIsAvailable()
    {
        return BashAvailability.Value;
    }

    private static readonly Lazy<bool> BashAvailability = new(ProbeBash);

    private static bool ProbeBash()
    {
        try
        {
            ProcessStartInfo startInfo = new("bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("exit 0");

            using Process probe = Process.Start(startInfo)!;
            probe.WaitForExit();
            return probe.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private sealed record VersionResult(
        int ExitCode,
        string Output,
        string Error,
        Dictionary<string, string> Values);
}

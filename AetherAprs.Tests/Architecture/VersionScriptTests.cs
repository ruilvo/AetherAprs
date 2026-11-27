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
    private const string BuildScriptRelativePath = ".github/scripts/build-release-apk.sh";

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

    [Fact]
    public void ReleaseBuildScriptPassesTheTagVersionToPublish()
    {
        VersionResult result = RunScriptFile(
            BuildScriptRelativePath,
            ["--dry-run"],
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["TAG"] = "v1.2.3" });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("-p:ApplicationVersion=1020399", result.Output, StringComparison.Ordinal);
        Assert.Contains("-p:ApplicationDisplayVersion=1.2.3", result.Output, StringComparison.Ordinal);
        Assert.Contains("-c Release", result.Output, StringComparison.Ordinal);
        Assert.Contains("-p:AndroidPackageFormat=apk", result.Output, StringComparison.Ordinal);

        // No signing material in the environment means the package uses the development key.
        Assert.DoesNotContain("-p:AndroidKeyStore=true", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseBuildScriptInvokesTheVersionScriptThroughBash()
    {
        // The version script is not guaranteed to carry the execute bit after a
        // checkout (Windows working copies and checkouts that ignore file modes
        // both drop it), so calling it directly fails with exit code 126 on Linux.
        // Invoking it through bash is mode-independent and works on every platform.
        string repositoryRoot = SourceScanner.FindRepositoryRoot();
        string script = File.ReadAllText(
            Path.Combine(repositoryRoot, BuildScriptRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains(
            "bash \"$REPO_ROOT/.github/scripts/extract-version.sh\"",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseBuildScriptSignsAndPinsBuildToolsOnlyWhenTheEnvironmentProvidesThem()
    {
        VersionResult result = RunScriptFile(
            BuildScriptRelativePath,
            ["--dry-run"],
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["TAG"] = "v1.2.3-beta.5",
                ["ANDROID_BUILD_TOOLS_VERSION"] = "34.0.0",
                ["AndroidSigningKeyStore"] = "/tmp/release.keystore",
            });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("-p:ApplicationVersion=1020338", result.Output, StringComparison.Ordinal);
        Assert.Contains("-p:ApplicationDisplayVersion=1.2.3-beta.5", result.Output, StringComparison.Ordinal);
        Assert.Contains("-p:AndroidSdkBuildToolsVersion=34.0.0", result.Output, StringComparison.Ordinal);
        Assert.Contains("-p:AndroidKeyStore=true", result.Output, StringComparison.Ordinal);

        // The keystore path itself never reaches the command line.
        Assert.DoesNotContain("release.keystore", result.Output, StringComparison.Ordinal);
    }

    private static VersionResult RunScript(string tag)
    {
        return RunScriptFile(ScriptRelativePath, [tag], new Dictionary<string, string?>(StringComparer.Ordinal));
    }

    private static VersionResult RunScriptFile(
        string scriptRelativePath,
        string[] arguments,
        Dictionary<string, string?> environment)
    {
        if (!BashIsAvailable())
        {
            Assert.Skip("bash is not available on this machine, so the release scripts cannot be run.");
        }

        string repositoryRoot = SourceScanner.FindRepositoryRoot();
        Assert.True(
            File.Exists(Path.Combine(repositoryRoot, scriptRelativePath.Replace('/', Path.DirectorySeparatorChar))),
            $"The release script is missing at {scriptRelativePath}.");

        ProcessStartInfo startInfo = new("bash")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(scriptRelativePath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (KeyValuePair<string, string?> variable in environment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

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

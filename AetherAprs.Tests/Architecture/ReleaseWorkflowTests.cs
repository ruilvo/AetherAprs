// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Guards the release workflow contract: one pinned Android build-tools version is
/// used for both the APK build and its verification, and the workflow publishes one
/// explicit signed APK instead of guessing the newest artifact in the output tree.
/// </summary>
public sealed class ReleaseWorkflowTests
{
    private static readonly string WorkflowPath = Path.Combine(
        SourceScanner.FindRepositoryRoot(),
        ".github",
        "workflows",
        "release-apk.yml");

    private const string BuildToolsVariable = "ANDROID_BUILD_TOOLS_VERSION";

    [Fact]
    public void BuildToolsVersionIsPinnedOnceForTheWholeWorkflow()
    {
        string workflow = ReadWorkflow();

        MatchCollection definitions = Regex.Matches(
            workflow,
            @"^\s*" + BuildToolsVariable + @":\s*'(?<version>\d+\.\d+\.\d+)'\s*$",
            RegexOptions.Multiline);
        Assert.True(
            definitions.Count == 1,
            $"The release workflow must define {BuildToolsVariable} exactly once with a pinned major.minor.patch value, "
            + $"but found {definitions.Count} definition(s).");

        Assert.True(
            Regex.IsMatch(workflow, @"AndroidSdkBuildToolsVersion=""\$" + BuildToolsVariable + @"""", RegexOptions.CultureInvariant),
            $"The APK build must take its build-tools version from ${BuildToolsVariable}.");
        Assert.False(
            Regex.IsMatch(workflow, @"AndroidSdkBuildToolsVersion=\d", RegexOptions.CultureInvariant),
            "The APK build must not hardcode a build-tools version; it is pinned once as a workflow variable.");
    }

    [Fact]
    public void ApkVerificationUsesTheSamePinnedBuildToolsVersion()
    {
        string verification = GetStepBlock(ReadWorkflow(), "Find and Verify APK");

        Assert.Contains($"$ANDROID_HOME/build-tools/${BuildToolsVariable}", verification, StringComparison.Ordinal);

        foreach (string tool in new[] { "zipalign", "apksigner", "aapt" })
        {
            Assert.Contains($"\"$BUILD_TOOLS_DIR/{tool}\"", verification, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            "find \"$ANDROID_HOME/build-tools\"",
            verification,
            StringComparison.Ordinal);
        Assert.False(
            Regex.IsMatch(verification, @"build-tools.*sort -V", RegexOptions.CultureInvariant),
            "The verification step must not pick the newest installed build-tools; it must use the pinned version.");
    }

    [Fact]
    public void SignedApkIsSelectedWithoutGuessing()
    {
        string verification = GetStepBlock(ReadWorkflow(), "Find and Verify APK");

        Assert.Contains("-name \"*-Signed.apk\"", verification, StringComparison.Ordinal);
        Assert.DoesNotContain("tail -n 1", verification, StringComparison.Ordinal);

        // Zero artifacts and more than one artifact are both hard failures.
        Assert.Contains("\"${#CANDIDATES[@]}\" -eq 0", verification, StringComparison.Ordinal);
        Assert.Contains("\"${#CANDIDATES[@]}\" -ne 1", verification, StringComparison.Ordinal);
        Assert.Contains("::error::", verification, StringComparison.Ordinal);
        Assert.Contains("apk_path=$APK_PATH", verification, StringComparison.Ordinal);
    }

    private static string ReadWorkflow()
    {
        Assert.True(File.Exists(WorkflowPath), $"The release workflow is missing at {WorkflowPath}.");
        return File.ReadAllText(WorkflowPath);
    }

    /// <summary>Extracts one step of the workflow, from its name up to the next step.</summary>
    private static string GetStepBlock(string workflow, string stepName)
    {
        const string stepIndent = "\n      - name: ";
        int start = workflow.IndexOf(stepIndent + stepName, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The release workflow no longer has a step named '{stepName}'.");

        int nextStep = workflow.IndexOf(stepIndent, start + stepIndent.Length + stepName.Length, StringComparison.Ordinal);
        return nextStep < 0 ? workflow[start..] : workflow[start..nextStep];
    }
}

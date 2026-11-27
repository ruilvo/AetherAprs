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
            Regex.IsMatch(workflow, @"AndroidSdkBuildToolsVersion=""\$" + BuildToolsVariable + @"""", RegexOptions.CultureInvariant)
            || Regex.IsMatch(ReadReleaseBuildScript(), @"AndroidSdkBuildToolsVersion=""\$" + BuildToolsVariable + @"""", RegexOptions.CultureInvariant),
            $"The APK build must take its build-tools version from ${BuildToolsVariable}.");
        Assert.False(
            Regex.IsMatch(workflow, @"AndroidSdkBuildToolsVersion=\d", RegexOptions.CultureInvariant)
            || Regex.IsMatch(ReadReleaseBuildScript(), @"AndroidSdkBuildToolsVersion=\d", RegexOptions.CultureInvariant),
            "The APK build must not hardcode a build-tools version; it is pinned once as a workflow variable.");
    }

    [Fact]
    public void TheBuildStepGoesThroughTheVersionedReleaseScript()
    {
        string workflow = ReadWorkflow();
        string build = GetStepBlock(workflow, "Build APK");

        // One publish site for CI and local builds, so the version always comes from the tag.
        Assert.Contains("./.github/scripts/build-release-apk.sh", build, StringComparison.Ordinal);
        Assert.DoesNotContain("-p:ApplicationVersion", build, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish", build, StringComparison.Ordinal);
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

    [Fact]
    public void SigningSecretsArePassedThroughTheEnvironment()
    {
        string workflow = ReadWorkflow();
        string build = GetStepBlock(workflow, "Build APK");

        // A secret on a command line is readable by any process on the runner and can
        // be echoed by MSBuild, so the Android signing properties come from the
        // environment instead, which MSBuild promotes to properties.
        Assert.DoesNotContain("-p:AndroidSigning", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "export AndroidSigningKeyStore=\"$ANDROID_KEYSTORE_PATH\"",
            build,
            StringComparison.Ordinal);
        Assert.Contains(
            "export AndroidSigningKeyAlias=\"$ANDROID_KEY_ALIAS\"",
            build,
            StringComparison.Ordinal);
        Assert.Contains(
            "export AndroidSigningKeyPass=\"$ANDROID_KEY_PASSWORD\"",
            build,
            StringComparison.Ordinal);
        Assert.Contains(
            "export AndroidSigningStorePass=\"$ANDROID_KEYSTORE_PASSWORD\"",
            build,
            StringComparison.Ordinal);

        // keytool can read the store password from the environment too.
        string verify = GetStepBlock(workflow, "Verify keystore");
        Assert.Contains("-storepass:env KEYSTORE_PASSWORD", verify, StringComparison.Ordinal);
        Assert.DoesNotContain("-storepass \"$KEYSTORE_PASSWORD\"", verify, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLogIsCheckedForLeakedSigningSecrets()
    {
        string build = GetStepBlock(ReadWorkflow(), "Build APK");

        Assert.Contains("tee \"$BUILD_LOG\"", build, StringComparison.Ordinal);
        Assert.Contains("grep -qF -- \"$secret\" \"$BUILD_LOG\"", build, StringComparison.Ordinal);
        Assert.Contains("::error::A signing password appeared in the build log", build, StringComparison.Ordinal);
    }

    [Fact]
    public void SigningMaterialIsRemovedAndTheWorkspaceChecked()
    {
        string cleanup = GetStepBlock(ReadWorkflow(), "Clean up signing material");

        Assert.Contains("if: always()", cleanup, StringComparison.Ordinal);
        Assert.Contains("-name \"*.keystore\"", cleanup, StringComparison.Ordinal);
        Assert.Contains("::error::Signing material was left in the workspace", cleanup, StringComparison.Ordinal);
    }

    [Fact]
    public void AReleaseIsRejectedWhenTheTagAlreadyHasOne()
    {
        string workflow = ReadWorkflow();
        string guard = GetStepBlock(workflow, "Reject a release that already exists for this tag");

        Assert.Contains("gh release view \"$TAG\"", guard, StringComparison.Ordinal);
        Assert.Contains("::error::Release $TAG already exists", guard, StringComparison.Ordinal);
        Assert.Contains("exit 1", guard, StringComparison.Ordinal);

        // The collision check must run before the keystore is decoded and used.
        Assert.True(
            workflow.IndexOf("Reject a release that already exists for this tag", StringComparison.Ordinal)
            < workflow.IndexOf("Decode keystore", StringComparison.Ordinal),
            "The release collision check must run before any signing material is decoded.");
    }

    [Fact]
    public void ApkVersionCodeIsVerifiedAgainstTheTag()
    {
        string verification = GetStepBlock(ReadWorkflow(), "Find and Verify APK");

        Assert.Contains("EXPECTED_VERSION_CODE", verification, StringComparison.Ordinal);
        Assert.Contains("versionCode='", verification, StringComparison.Ordinal);
        Assert.Contains("does not match", verification, StringComparison.Ordinal);
    }

    [Fact]
    public void SbomIsGeneratedFromTheResolvedBuildAndShippedWithTheApk()
    {
        string workflow = ReadWorkflow();
        string sbom = GetStepBlock(workflow, "Generate and verify SBOM");

        // The document must describe the dependency graph this build resolved.
        Assert.Contains(
            "--project AetherAprs.Android/AetherAprs.Android.csproj",
            sbom,
            StringComparison.Ordinal);
        Assert.Contains("generate_sbom.py generate", sbom, StringComparison.Ordinal);
        Assert.Contains("generate_sbom.py verify", sbom, StringComparison.Ordinal);
        Assert.Contains("--apk \"$APK_NAME\"", sbom, StringComparison.Ordinal);

        // An SBOM nobody can download proves nothing about the published APK.
        string release = GetStepBlock(workflow, "Create Release");
        Assert.Contains("steps.sbom.outputs.sbom_name", release, StringComparison.Ordinal);
        Assert.Contains("steps.sbom.outputs.sbom_checksum_name", release, StringComparison.Ordinal);

        // The SBOM records the APK checksum, so the APK must exist first.
        Assert.True(
            workflow.IndexOf("Generate and verify SBOM", StringComparison.Ordinal)
            > workflow.IndexOf("Find and Verify APK", StringComparison.Ordinal),
            "The SBOM must be generated after the signed APK it describes.");
    }

    private static string ReadWorkflow()
    {
        Assert.True(File.Exists(WorkflowPath), $"The release workflow is missing at {WorkflowPath}.");
        return File.ReadAllText(WorkflowPath);
    }

    private static string ReadReleaseBuildScript()
    {
        string path = Path.Combine(
            SourceScanner.FindRepositoryRoot(),
            ".github",
            "scripts",
            "build-release-apk.sh");
        Assert.True(File.Exists(path), $"The release build script is missing at {path}.");
        return File.ReadAllText(path);
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

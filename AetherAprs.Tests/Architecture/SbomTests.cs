// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

using Xunit;

namespace AetherAprs.Tests.Architecture;

/// <summary>
/// Guards the SBOM contract: the document is generated from the dependency report of
/// the actual build and it provably describes the APK that ships with it.
/// </summary>
public sealed class SbomTests
{
    private const string ScriptRelativePath = ".github/scripts/generate_sbom.py";

    // Fixture data for the synthetic dependency report. The tests never assert a real
    // release version, so bumping the application version cannot break them.
    private const string FixturePackageId = "Avalonia.Android";
    private const string FixturePackageVersion = "12.2.0";
    private const string FixtureVersionName = "0.0.0-sbom-fixture";
    private const int FixtureVersionCode = 1;

    private static readonly Lazy<string?> PythonExecutable = new(ProbePython);

    [Fact]
    public void GeneratorDescribesEachResolvedPackageOnce()
    {
        using TemporaryDirectory directory = new();
        string apk = directory.Write("app.apk", "apk-bytes");

        GeneratedRelease release = CreateRelease(directory);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(release.Sbom));
        JsonElement root = document.RootElement;
        Assert.Equal("CycloneDX", root.GetProperty("bomFormat").GetString());
        Assert.Matches(@"^1\.\d+$", root.GetProperty("specVersion").GetString());

        // The package appears as top-level and transitive in two frameworks; it must be
        // listed once, keyed by the version that was resolved.
        JsonElement components = root.GetProperty("components");
        Assert.Equal(1, components.GetArrayLength());
        Assert.Equal(FixturePackageId, components[0].GetProperty("name").GetString());
        Assert.Equal(
            $"pkg:nuget/{FixturePackageId}@{FixturePackageVersion}",
            components[0].GetProperty("purl").GetString());

        JsonElement application = root.GetProperty("metadata").GetProperty("component");
        Assert.Equal("AetherAprs", application.GetProperty("name").GetString());
        Assert.Equal(FixtureVersionName, application.GetProperty("version").GetString());
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(apk))),
            application.GetProperty("hashes")[0].GetProperty("content").GetString());
    }

    [Fact]
    public void VerificationPassesForTheDocumentedArtifact()
    {
        using TemporaryDirectory directory = new();
        GeneratedRelease release = CreateRelease(directory);

        // Verify with the values the document itself reports, so the check proves the
        // document and the artifact agree rather than restating a hardcoded version.
        (string versionName, int versionCode) = ReadVersion(release.Sbom);
        ScriptResult result = RunPython(
            ["verify", "--sbom", release.Sbom, "--version-name", versionName, "--version-code", Code(versionCode), "--apk", release.Apk]);

        Assert.True(result.ExitCode == 0, result.Combined);
        Assert.Contains($"describes AetherAprs {versionName}", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationRejectsAChecksumFromAnotherArtifact()
    {
        using TemporaryDirectory directory = new();
        GeneratedRelease release = CreateRelease(directory);
        string otherApk = directory.Write("other.apk", "different-bytes");
        (string versionName, int versionCode) = ReadVersion(release.Sbom);

        ScriptResult result = RunPython(
            ["verify", "--sbom", release.Sbom, "--version-name", versionName, "--version-code", Code(versionCode), "--apk", otherApk]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("checksum does not match", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationRejectsADocumentForAnotherVersionNumber()
    {
        using TemporaryDirectory directory = new();
        GeneratedRelease release = CreateRelease(directory);
        (string versionName, int versionCode) = ReadVersion(release.Sbom);

        ScriptResult result = RunPython(
            [
                "verify",
                "--sbom", release.Sbom,
                "--version-name", versionName + "-other",
                "--version-code", Code(versionCode),
                "--apk", release.Apk,
            ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("SBOM version is", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationRejectsADocumentForAnotherVersionCode()
    {
        using TemporaryDirectory directory = new();
        GeneratedRelease release = CreateRelease(directory);
        (string versionName, int versionCode) = ReadVersion(release.Sbom);

        ScriptResult result = RunPython(
            [
                "verify",
                "--sbom", release.Sbom,
                "--version-name", versionName,
                "--version-code", Code(versionCode + 1),
                "--apk", release.Apk,
            ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("SBOM version code is", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBuildWorkflowChecksSbomGenerationOnEveryPush()
    {
        string path = Path.Combine(SourceScanner.FindRepositoryRoot(), ".github", "workflows", "build-test.yml");
        Assert.True(File.Exists(path));

        string workflow = File.ReadAllText(path);
        Assert.Contains("generate_sbom.py generate", workflow, StringComparison.Ordinal);
        Assert.Contains("generate_sbom.py verify", workflow, StringComparison.Ordinal);
    }

    private static GeneratedRelease CreateRelease(TemporaryDirectory directory)
    {
        string report = directory.Write("report.json", DependencyReport());
        string apk = directory.Write("app.apk", "apk-bytes");
        string sbom = directory.PathOf("app.cdx.json");

        ScriptResult generate = RunPython(
            [
                "generate",
                "--report", report,
                "--output", sbom,
                "--version-name", FixtureVersionName,
                "--version-code", Code(FixtureVersionCode),
                "--apk", apk,
            ]);
        Assert.True(generate.ExitCode == 0, generate.Combined);
        return new GeneratedRelease(sbom, apk);
    }

    private static string DependencyReport() =>
        $$"""
        {
          "projects": [
            {
              "frameworks": [
                {
                  "framework": "net11.0-android",
                  "topLevelPackages": [
                    { "id": "{{FixturePackageId}}", "resolvedVersion": "{{FixturePackageVersion}}" }
                  ],
                  "transitivePackages": [
                    { "id": "{{FixturePackageId}}", "resolvedVersion": "{{FixturePackageVersion}}" }
                  ]
                },
                {
                  "framework": "net11.0-android34.0",
                  "topLevelPackages": [
                    { "id": "{{FixturePackageId}}", "resolvedVersion": "{{FixturePackageVersion}}" }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private static (string VersionName, int VersionCode) ReadVersion(string sbom)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(sbom));
        JsonElement application = document.RootElement.GetProperty("metadata").GetProperty("component");
        string versionName = application.GetProperty("version").GetString()
            ?? throw new InvalidOperationException("The SBOM does not report a version.");
        int versionCode = 0;
        foreach (JsonElement property in application.GetProperty("properties").EnumerateArray())
        {
            if (property.GetProperty("name").GetString() == "aether-aprs:version-code")
            {
                versionCode = int.Parse(property.GetProperty("value").GetString()!, CultureInfo.InvariantCulture);
            }
        }

        Assert.True(versionCode > 0, "The SBOM does not report a version code.");
        return (versionName, versionCode);
    }

    private static string Code(int versionCode) => versionCode.ToString(CultureInfo.InvariantCulture);

    private static ScriptResult RunPython(string[] arguments)
    {
        string? python = PythonExecutable.Value;
        if (python is null)
        {
            Assert.Skip("Python is not installed on this machine, so the SBOM script cannot be executed.");
        }

        ProcessStartInfo startInfo = new(python)
        {
            WorkingDirectory = SourceScanner.FindRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(ScriptRelativePath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the SBOM script.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ScriptResult(process.ExitCode, output, error);
    }

    private static string? ProbePython()
    {
        foreach (string candidate in (string[])["python3", "python"])
        {
            try
            {
                ProcessStartInfo startInfo = new(candidate)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("import sys; sys.exit(0)");

                using Process process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Process did not start.");
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch (Win32Exception)
            {
                // Try the next candidate name.
            }
        }

        return null;
    }

    private sealed record GeneratedRelease(string Sbom, string Apk);

    private sealed record ScriptResult(int ExitCode, string Output, string Error)
    {
        public string Combined => Output + Error;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "aether-aprs-sbom-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string PathOf(string name) => Path.Combine(Root, name);

        public string Write(string name, string content)
        {
            string path = PathOf(name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test over.
            }
        }
    }
}

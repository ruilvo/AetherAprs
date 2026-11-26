// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AetherAprs.Tests.Services.Configuration;

/// <summary>
/// End-to-end tests for the on-disk schema versioning of appsettings.json:
/// how unversioned files are upgraded, how the version is persisted, and how
/// the app copes with settings written by a newer or corrupted build.
/// </summary>
public sealed class SettingsVersioningTests
{
    [Fact]
    public void UnversionedFile_LoadsAsLegacyVersion()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write("{\"Aprs\":{\"Callsign\":\"CT7ALW\"}}");

        Assert.Equal(
            SettingsSchema.LegacyUnversionedVersion,
            fixture.ReadInputDocumentVersion());

        var service = fixture.CreateService();

        Assert.Equal("CT7ALW", service.Current.Aprs.Callsign);
    }

    [Fact]
    public void UnversionedFile_IsUpgradedToCurrentVersionInMemory()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write("{\"Aprs\":{\"Callsign\":\"CT7ALW\"}}");

        var service = fixture.CreateService();

        Assert.Equal(SettingsSchema.CurrentVersion, service.Current.SchemaVersion);
    }

    [Fact]
    public async Task Save_WritesCurrentSchemaVersion()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write("{\"Aprs\":{\"Callsign\":\"CT7ALW\"}}");
        var service = fixture.CreateService();

        await service.UpdateAsync(settings => settings, TestContext.Current.CancellationToken);

        Assert.Equal(SettingsSchema.CurrentVersion, fixture.ReadDocumentVersion());
        Assert.Contains(
            $"\"{SettingsSchema.VersionPropertyName}\"",
            fixture.ReadSavedText());
    }

    [Fact]
    public async Task SaveThenReload_PreservesAllSettings()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write("{\"Aprs\":{\"Callsign\":\"N0CALL\"}}");
        var service = fixture.CreateService();

        await service.UpdateAsync(settings => settings with
        {
            Aprs = settings.Aprs with
            {
                Callsign = "CT7ALW",
                Ssid = AprsSsid.HumanPortable,
                SymbolTable = SymbolTable.Alternate,
                SymbolCode = SymbolCode.GreaterThanSign,
                DisplayTimeRange = PacketDisplayTimeRange.LastWeek,
                LoadHistoryOnStartup = false,
                PositionFormat = PositionFormat.MicE,
            },
            Theme = ThemeMode.Dark,
            Map = settings.Map with { TileSource = TileSourceType.MbTilesOffline },
        }, TestContext.Current.CancellationToken);

        var reloaded = fixture.CreateService();

        Assert.Equal(SettingsSchema.CurrentVersion, reloaded.Current.SchemaVersion);
        Assert.Equal("CT7ALW", reloaded.Current.Aprs.Callsign);
        Assert.Equal(AprsSsid.HumanPortable, reloaded.Current.Aprs.Ssid);
        Assert.Equal(SymbolTable.Alternate, reloaded.Current.Aprs.SymbolTable);
        Assert.Equal(SymbolCode.GreaterThanSign, reloaded.Current.Aprs.SymbolCode);
        Assert.Equal(PacketDisplayTimeRange.LastWeek, reloaded.Current.Aprs.DisplayTimeRange);
        Assert.False(reloaded.Current.Aprs.LoadHistoryOnStartup);
        Assert.Equal(PositionFormat.MicE, reloaded.Current.Aprs.PositionFormat);
        Assert.Equal(ThemeMode.Dark, reloaded.Current.Theme);
        Assert.Equal(TileSourceType.MbTilesOffline, reloaded.Current.Map.TileSource);
    }

    [Fact]
    public void MigratingUnversionedFile_CreatesBackupOfOriginal()
    {
        using var fixture = new SettingsDirectoryFixture();
        const string legacyJson = "{\"Aprs\":{\"Callsign\":\"CT7ALW\"}}";
        fixture.Write(legacyJson);

        fixture.CreateService();

        var backups = fixture.GetBackupFiles();
        var backup = Assert.Single(backups);
        Assert.Contains(".v0.", backup);
        Assert.Equal(legacyJson, File.ReadAllText(backup));
    }

    [Fact]
    public async Task ReloadingCurrentVersionFile_DoesNotAccumulateBackups()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write("{\"Aprs\":{\"Callsign\":\"CT7ALW\"}}");
        var first = fixture.CreateService();
        await first.UpdateAsync(settings => settings, TestContext.Current.CancellationToken);

        // The unversioned base file produces exactly one pre-migration backup;
        // later loads must not add more.
        for (var i = 0; i < 3; i++)
        {
            fixture.CreateService();
        }

        Assert.Single(fixture.GetBackupFiles());
    }

    [Fact]
    public void LegacySymbolKeys_AreMigratedOnLoad()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write(
            "{\"Aprs\":{\"Callsign\":\"CT7ALW\","
            + "\"SymbolTableCharacter\":\"\\\\\","
            + "\"SymbolCodeCharacter\":\">\"}}");

        var service = fixture.CreateService();

        Assert.Equal(SymbolTable.Alternate, service.Current.Aprs.SymbolTable);
        Assert.Equal(SymbolCode.GreaterThanSign, service.Current.Aprs.SymbolCode);
    }

    [Fact]
    public async Task LegacySymbolKeys_SurviveRoundTripAfterMigration()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write(
            "{\"Aprs\":{\"Callsign\":\"CT7ALW\","
            + "\"SymbolTableCharacter\":\"\\\\\","
            + "\"SymbolCodeCharacter\":\">\"}}");
        var service = fixture.CreateService();

        await service.UpdateAsync(settings => settings, TestContext.Current.CancellationToken);
        var reloaded = fixture.CreateService();

        Assert.Equal(SymbolTable.Alternate, reloaded.Current.Aprs.SymbolTable);
        Assert.Equal(SymbolCode.GreaterThanSign, reloaded.Current.Aprs.SymbolCode);
    }

    [Fact]
    public void InvalidSymbolValue_DoesNotPreventStartup()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write(
            "{\"Aprs\":{\"Callsign\":\"CT7ALW\",\"SymbolTableCharacter\":\"?\"}}");

        var service = fixture.CreateService();

        Assert.Equal("CT7ALW", service.Current.Aprs.Callsign);
        Assert.Equal(SymbolTable.Primary, service.Current.Aprs.SymbolTable);
    }

    [Fact]
    public void NewerVersionedFile_IsLoadedReadOnly()
    {
        using var fixture = new SettingsDirectoryFixture();
        var futureVersion = SettingsSchema.CurrentVersion + 5;
        fixture.Write(
            "{\"SchemaVersion\":" + futureVersion + ",\"Aprs\":{\"Callsign\":\"CT7ALW\"}}");

        var service = fixture.CreateService();

        // Values still load for use, but the document version is preserved and
        // the service refuses to write it back in the older format.
        Assert.Equal("CT7ALW", service.Current.Aprs.Callsign);
        Assert.True(service.IsReadOnly);
        Assert.Equal(futureVersion, service.Current.SchemaVersion);
        Assert.Equal(futureVersion, fixture.ReadDocumentVersion());
    }

    [Fact]
    public async Task NewerVersionedFile_RefusesEveryWriteAndLeavesFileUnchanged()
    {
        using var fixture = new SettingsDirectoryFixture();
        var futureVersion = SettingsSchema.CurrentVersion + 4;
        var json = "{\"SchemaVersion\":" + futureVersion
            + ",\"Aprs\":{\"Callsign\":\"CT7ALW\"}}";
        fixture.Write(json);

        var logger = new RecordingLogger<ConfigurationService>();
        var service = fixture.CreateService(logger);
        var settingsChangedCount = 0;
        service.SettingsChanged += (_, _) => settingsChangedCount++;

        var inputBefore = File.ReadAllBytes(fixture.InputPath);
        var savedPath = fixture.SavedPath;
        var savedBefore = File.Exists(savedPath)
            ? File.ReadAllBytes(savedPath)
            : null;

        await service.UpdateAsync(settings => settings with
        {
            Aprs = settings.Aprs with { Callsign = "N0CALL" },
        }, TestContext.Current.CancellationToken);

        Assert.True(service.IsReadOnly);
        Assert.Equal(inputBefore, File.ReadAllBytes(fixture.InputPath));
        if (savedBefore is null)
        {
            Assert.False(File.Exists(savedPath));
        }
        else
        {
            Assert.Equal(savedBefore, File.ReadAllBytes(savedPath));
        }

        // UpdateAsync refuses before invoking the action.
        Assert.Equal("CT7ALW", service.Current.Aprs.Callsign);
        Assert.Equal(0, settingsChangedCount);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains(
                    nameof(ConfigurationService.UpdateAsync),
                    StringComparison.Ordinal));
    }

    [Fact]
    public void NewerVersionedFile_IsBackedUpAndLeftUntouched()
    {
        using var fixture = new SettingsDirectoryFixture();
        var futureVersion = SettingsSchema.CurrentVersion + 3;
        var json = "{\"SchemaVersion\":" + futureVersion + "}";
        fixture.Write(json);
        var service = fixture.CreateService();

        var backup = Assert.Single(fixture.GetBackupFiles());
        Assert.Contains($".v{futureVersion}.", backup);
        Assert.True(service.IsReadOnly);
        Assert.Equal(futureVersion, service.Current.SchemaVersion);
        Assert.Equal(json, File.ReadAllText(fixture.InputPath));
    }

    [Fact]
    public void LegacyFile_IsMigratedAndPersistedImmediately()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write(
            "{\"SchemaVersion\":0,\"Aprs\":{\"Callsign\":\"CT7ALW\"}}");

        var service = fixture.CreateService();

        // Migration succeeds; the document is rewritten before any explicit save.
        Assert.Equal(SettingsSchema.CurrentVersion, service.Current.SchemaVersion);
        Assert.Equal("CT7ALW", service.Current.Aprs.Callsign);
        Assert.True(File.Exists(fixture.SavedPath));
        Assert.Equal(SettingsSchema.CurrentVersion, fixture.ReadDocumentVersion());
    }

    [Fact]
    public void InvalidVersionValue_FallsBackToLegacy()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write("{\"SchemaVersion\":\"not-a-number\",\"Aprs\":{\"Callsign\":\"CT7ALW\"}}");

        var service = fixture.CreateService();

        Assert.Equal("CT7ALW", service.Current.Aprs.Callsign);
        Assert.Equal(SettingsSchema.CurrentVersion, service.Current.SchemaVersion);
    }

    [Fact]
    public void NegativeVersionValue_FallsBackToLegacy()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write("{\"SchemaVersion\":-4,\"Aprs\":{\"Callsign\":\"CT7ALW\"}}");

        var service = fixture.CreateService();

        Assert.Equal("CT7ALW", service.Current.Aprs.Callsign);
        Assert.Equal(SettingsSchema.CurrentVersion, service.Current.SchemaVersion);
    }

    [Fact]
    public void CorruptFile_IsQuarantinedAndDefaultsUsed()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write("{ this is not json ");

        var service = fixture.CreateService();

        Assert.Equal(SettingsSchema.CurrentVersion, service.Current.SchemaVersion);
        Assert.Equal("N0CALL", service.Current.Aprs.Callsign);
        Assert.NotEmpty(fixture.GetQuarantinedFiles());
    }

    [Fact]
    public async Task LoadedSnapshot_CarriesVersionAndNewProperties()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write("{\"Aprs\":{\"Callsign\":\"CT7ALW\"}}");
        var service = fixture.CreateService();

        var snapshot = await service.UpdateAsync(settings => settings with
        {
            Aprs = settings.Aprs with
            {
                LoadHistoryOnStartup = false,
                PacketRetentionMaxRows = 4242,
            },
        }, TestContext.Current.CancellationToken);

        Assert.Equal(SettingsSchema.CurrentVersion, snapshot.SchemaVersion);
        Assert.False(snapshot.Aprs.LoadHistoryOnStartup);
        Assert.Equal(4242, snapshot.Aprs.PacketRetentionMaxRows);
    }

    [Fact]
    public void Constants_HaveSensibleRelationship()
    {
        Assert.True(SettingsSchema.CurrentVersion > SettingsSchema.LegacyUnversionedVersion);
        Assert.Equal(0, SettingsSchema.LegacyUnversionedVersion);
        Assert.Equal("SchemaVersion", SettingsSchema.VersionPropertyName);
    }

    [Fact]
    public async Task RepeatedLoadsAndSaves_KeepVersionStable()
    {
        using var fixture = new SettingsDirectoryFixture();
        fixture.Write("{\"Aprs\":{\"Callsign\":\"CT7ALW\"}}");

        for (var i = 0; i < 3; i++)
        {
            var service = fixture.CreateService();
            Assert.Equal(SettingsSchema.CurrentVersion, service.Current.SchemaVersion);
            await service.UpdateAsync(settings => settings, TestContext.Current.CancellationToken);
            Assert.Equal(SettingsSchema.CurrentVersion, fixture.ReadDocumentVersion());
        }

        // Only the very first (unversioned) load should have produced a backup.
        Assert.Single(fixture.GetBackupFiles());
    }

    /// <summary>
    /// Creates an isolated app-data directory and the services that write into it.
    /// </summary>
    private sealed class SettingsDirectoryFixture : IDisposable
    {
        private readonly string _directory;

        public SettingsDirectoryFixture()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                $"AetherAprsSettings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
        }

        public void Write(string json)
        {
            File.WriteAllText(InputPath, json);
        }

        public ConfigurationService CreateService()
        {
            return CreateService(NullLogger<ConfigurationService>.Instance);
        }

        public ConfigurationService CreateService(
            ILogger<ConfigurationService> logger)
        {
            return new ConfigurationService(
                new TestAppDataDirProvider(_directory),
                logger);
        }

        public string InputPath =>
            Path.Combine(_directory, "appsettings.json");

        public string SavedPath
        {
            get
            {
#if DEBUG
                return Path.Combine(_directory, "appsettings.Development.json");
#else
                return Path.Combine(_directory, "appsettings.json");
#endif
            }
        }

        public string ReadSavedText() => File.ReadAllText(SavedPath);

        public int ReadDocumentVersion()
        {
            var path = SavedPath;
            if (!File.Exists(path))
            {
                // No save has happened yet, so inspect the input file instead.
                path = InputPath;
            }

            return ReadVersion(path);
        }

        public int ReadInputDocumentVersion() => ReadVersion(InputPath);

        private static int ReadVersion(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty(
                SettingsSchema.VersionPropertyName,
                out var version)
                ? version.GetInt32()
                : SettingsSchema.LegacyUnversionedVersion;
        }

        public string[] GetBackupFiles() =>
            Directory.GetFiles(_directory, "appsettings.json.v*.bak");

        public string[] GetQuarantinedFiles() =>
            Directory.GetFiles(_directory, "appsettings.json.corrupt-*");

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private sealed class TestAppDataDirProvider(string directory) : IAppDataDirProviderService
    {
        public string GetAppDataDirectory() => directory;

        public string GetPrivateAppDataDirectory() => directory;
    }
}

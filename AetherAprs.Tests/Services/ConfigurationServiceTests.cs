// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading.Tasks;
using AetherAprs.Services;
using Xunit;

namespace AetherAprs.Tests.Services;

public sealed class ConfigurationServiceTests
{
    [Fact]
    public async Task SettingsRoundTripPersistsAprsSettings()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                "{\"Aprs\":{\"Callsign\":\"N0CALL\"}}");
            var provider = new TestAppDataDirProvider(directory);
            var service = new ConfigurationService(provider);

            service.Settings.Aprs.Callsign = "CT7ALW";
            service.Settings.Aprs.DefaultSymbolTableCharacter = "\\";
            service.Settings.Aprs.DefaultSymbolCodeCharacter = ">";
            service.Settings.Aprs.DefaultBeaconMode = AetherAprs.Models.DynamicBeaconMode.Drive;
            await service.SaveSettingsAsync();

#if DEBUG
            var savedPath = Path.Combine(directory, "appsettings.Development.json");
#else
            var savedPath = Path.Combine(directory, "appsettings.json");
#endif
            Assert.True(File.Exists(savedPath));
            var reloaded = new ConfigurationService(provider);
            Assert.Equal("CT7ALW", reloaded.Settings.Aprs.Callsign);
            Assert.Equal("\\", reloaded.Settings.Aprs.DefaultSymbolTableCharacter);
            Assert.Equal(">", reloaded.Settings.Aprs.DefaultSymbolCodeCharacter);
            Assert.Equal(AetherAprs.Models.DynamicBeaconMode.Drive, reloaded.Settings.Aprs.DefaultBeaconMode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ConstructorLoadsAprsSymbolDefaults()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                "{\"Aprs\":{\"Callsign\":\"N0CALL\"}}");
            var service = new ConfigurationService(new TestAppDataDirProvider(directory));

            Assert.Equal("/", service.Settings.Aprs.DefaultSymbolTableCharacter);
            Assert.Equal("[", service.Settings.Aprs.DefaultSymbolCodeCharacter);
            Assert.Equal(AetherAprs.Models.DynamicBeaconMode.Walk, service.Settings.Aprs.DefaultBeaconMode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AetherAprsTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class TestAppDataDirProvider(string directory) : IAppDataDirProviderService
    {
        public string GetAppDataDirectory() => directory;
    }
}

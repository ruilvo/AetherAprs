// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading.Tasks;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Platform;
using AetherAprs.Models.Aprs;
using Xunit;

namespace AetherAprs.Tests.Services.Configuration;

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
            service.Settings.Aprs.SymbolTable = SymbolTable.Alternate;
            service.Settings.Aprs.SymbolCode = SymbolCode.GreaterThanSign;
            await service.SaveSettingsAsync();

#if DEBUG
            var savedPath = Path.Combine(directory, "appsettings.Development.json");
#else
            var savedPath = Path.Combine(directory, "appsettings.json");
#endif
            Assert.True(File.Exists(savedPath));
            Assert.Contains(
                "\"SymbolTableCharacter\"",
                File.ReadAllText(savedPath));
            var reloaded = new ConfigurationService(provider);
            Assert.Equal("CT7ALW", reloaded.Settings.Aprs.Callsign);
            Assert.Equal(SymbolTable.Alternate, reloaded.Settings.Aprs.SymbolTable);
            Assert.Equal(SymbolCode.GreaterThanSign, reloaded.Settings.Aprs.SymbolCode);
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

            Assert.Equal(SymbolTable.Primary, service.Settings.Aprs.SymbolTable);
            Assert.Equal(SymbolCode.LeftSquareBracket, service.Settings.Aprs.SymbolCode);
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

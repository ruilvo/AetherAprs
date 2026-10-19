// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Threading.Tasks;
using AetherAprs.Configuration.Settings;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Platform;
using AetherAprs.Models.Aprs;
using Microsoft.Extensions.Logging.Abstractions;
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
            var service = new ConfigurationService(provider, NullLogger<ConfigurationService>.Instance);

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
            var reloaded = new ConfigurationService(provider, NullLogger<ConfigurationService>.Instance);
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
            var service = new ConfigurationService(new TestAppDataDirProvider(directory), NullLogger<ConfigurationService>.Instance);

            Assert.Equal(SymbolTable.Primary, service.Settings.Aprs.SymbolTable);
            Assert.Equal(SymbolCode.LeftSquareBracket, service.Settings.Aprs.SymbolCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_SeedsDefaultTemplate_WhenSectionAbsent()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                "{\"Logging\":{\"LogLevel\":{\"Default\":\"Information\"}}}");
            var provider = new TestAppDataDirProvider(directory);
            var service = new ConfigurationService(provider, NullLogger<ConfigurationService>.Instance);

            var template = Assert.Single(service.Settings.MessageTemplates.Templates);
            Assert.Equal("APRS2SOTA Spot", template.Name);
            Assert.Equal("SOTA", template.Destination);
            Assert.Equal("{summit} {freq} {mode} {comment}", template.Body);

#if DEBUG
            var savedPath = Path.Combine(directory, "appsettings.Development.json");
#else
            var savedPath = Path.Combine(directory, "appsettings.json");
#endif
            Assert.True(File.Exists(savedPath));
            Assert.Contains("\"MessageTemplates\"", File.ReadAllText(savedPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_DoesNotReseed_WhenSectionPresentButEmpty()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                "{\"MessageTemplates\":{\"Templates\":[]}}");
            var service = new ConfigurationService(new TestAppDataDirProvider(directory), NullLogger<ConfigurationService>.Instance);

            Assert.Empty(service.Settings.MessageTemplates.Templates);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_SanitizesInvalidTemplates()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                "{\"MessageTemplates\":{\"Templates\":["
                + "{\"Name\":\"Summit\",\"Destination\":\"SOTA\",\"Body\":\"{summit} on {freq}\"},"
                + "{\"Name\":\"Invalid\",\"Destination\":\"not valid!!\",\"Body\":\"x\"},"
                + "{\"Name\":\"summit\",\"Destination\":\"N0CALL-1\",\"Body\":\"y\"}]}}");
            var service = new ConfigurationService(new TestAppDataDirProvider(directory), NullLogger<ConfigurationService>.Instance);

            var template = Assert.Single(service.Settings.MessageTemplates.Templates);
            Assert.Equal("Summit", template.Name);
            Assert.Equal("SOTA", template.Destination);
            Assert.Equal("{summit} on {freq}", template.Body);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetSettingsSnapshot_ClonesMessageTemplates()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                "{\"MessageTemplates\":{\"Templates\":["
                + "{\"Name\":\"Spot\",\"Destination\":\"SOTA\",\"Body\":\"hello\"}]}}");
            var service = new ConfigurationService(new TestAppDataDirProvider(directory), NullLogger<ConfigurationService>.Instance);

            var snapshot = service.GetSettingsSnapshot();
            var cloned = Assert.Single(snapshot.MessageTemplates.Templates);
            cloned.Name = "Mutated Name";
            cloned.Destination = "N0CALL-9";
            cloned.Body = "mutated body";
            snapshot.MessageTemplates.Templates.Add(
                new MessageTemplate { Name = "Extra", Destination = "N0CALL", Body = "extra" });

            var live = Assert.Single(service.Settings.MessageTemplates.Templates);
            Assert.Equal("Spot", live.Name);
            Assert.Equal("SOTA", live.Destination);
            Assert.Equal("hello", live.Body);
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

        public string GetPrivateAppDataDirectory() => directory;
    }
}


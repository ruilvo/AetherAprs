// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Platform;
using AetherAprs.Models.Aprs;
using Microsoft.Extensions.Logging;
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
    public void Constructor_ReportsEachInvalidValueAndBindsTheRemainder()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                "{\"Aprs\":{\"Callsign\":\"TOOLONGCALL\",\"Ssid\":99,"
                + "\"PacketRetentionDays\":45,\"BeaconComment\":\"kept\"}}");
            var logger = new RecordingLogger<ConfigurationService>();
            var service = new ConfigurationService(
                new TestAppDataDirProvider(directory),
                logger);

            // Both invalid values fall back to their defaults...
            Assert.Equal("N0CALL", service.Settings.Aprs.Callsign);
            Assert.Equal(AprsSsid.PrimaryStation, service.Settings.Aprs.Ssid);

            // ...without discarding the rest of the document.
            Assert.Equal(45, service.Settings.Aprs.PacketRetentionDays);
            Assert.Equal("kept", service.Settings.Aprs.BeaconComment);

            var invalidWarnings = logger.Entries
                .Where(entry => entry.Level == LogLevel.Warning
                    && entry.Message.Contains(
                        "Ignoring invalid settings value",
                        StringComparison.Ordinal))
                .ToList();
            Assert.Equal(2, invalidWarnings.Count);
            Assert.Contains(
                invalidWarnings,
                entry => entry.Message.Contains(
                    "Aprs:Callsign",
                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                invalidWarnings,
                entry => entry.Message.Contains(
                    "Aprs:Ssid",
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_LoadsTemplatesAndFieldDefaultsFromAppSettings()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                "{\"MessageTemplates\":{\"Templates\":["
                + "{\"Name\":\"APRS2SOTA Spot\",\"Destination\":\"SOTA\","
                + "\"Body\":\"{summit} {freq} {mode} {comment}\","
                + "\"FieldDefaults\":{\"summit\":\"W7A/XX-001\",\"freq\":\"144.300\","
                + "\"mode\":\"FM\",\"comment\":\"SOTA\"}}]}}");
            var provider = new TestAppDataDirProvider(directory);
            var service = new ConfigurationService(provider, NullLogger<ConfigurationService>.Instance);

            var template = Assert.Single(service.Settings.MessageTemplates.Templates);
            Assert.Equal("APRS2SOTA Spot", template.Name);
            Assert.Equal("SOTA", template.Destination);
            Assert.Equal("{summit} {freq} {mode} {comment}", template.Body);
            Assert.Equal("144.300", template.FieldDefaults["freq"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_PreservesEmptyTemplatesFromAppSettings()
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
    public async Task ThemeRoundTripPersistsThemeMode()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                "{\"Aprs\":{\"Callsign\":\"N0CALL\"}}");
            var provider = new TestAppDataDirProvider(directory);
            var service = new ConfigurationService(provider, NullLogger<ConfigurationService>.Instance);

            // Default is System (follow OS).
            Assert.Equal(ThemeMode.System, service.Settings.Theme);

            service.Settings.Theme = ThemeMode.Dark;
            await service.SaveSettingsAsync();

            var reloaded = new ConfigurationService(provider, NullLogger<ConfigurationService>.Instance);
            Assert.Equal(ThemeMode.Dark, reloaded.Settings.Theme);
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

    [Fact]
    public async Task UpdateSettingsAsync_SerializesConcurrentPartialUpdates()
    {
        using var fixture = new ConfigurationFixture();
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var cancellationToken = TestContext.Current.CancellationToken;
        var first = Task.Run(() => fixture.Service.UpdateSettingsAsync(settings =>
        {
            settings.Aprs.Callsign = "FIRST";
            firstEntered.SetResult();
            releaseFirst.Task.WaitAsync(TestTimeouts.TestTimeout, cancellationToken)
                .GetAwaiter().GetResult();
        }), cancellationToken);

        var second = Task.CompletedTask;
        try
        {
            await firstEntered.Task.WaitAsync(TestTimeouts.TestTimeout, cancellationToken);
            second = fixture.Service.UpdateSettingsAsync(settings => settings.Theme = ThemeMode.Dark);
            Assert.False(second.IsCompleted);
            Assert.Equal("N0CALL", fixture.Service.GetSettingsSnapshot().Aprs.Callsign);
        }
        finally
        {
            releaseFirst.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TestTimeouts.TestTimeout, cancellationToken);
        }

        using var reloaded = fixture.Reload();
        Assert.Equal("FIRST", reloaded.Settings.Aprs.Callsign);
        Assert.Equal(ThemeMode.Dark, reloaded.Settings.Theme);
    }

    [Fact]
    public async Task UpdateSettingsAsync_WhenPatchThrows_LeavesCommittedSettingsUnchanged()
    {
        using var fixture = new ConfigurationFixture();
        var changes = 0;
        fixture.Service.SettingsChanged += (_, _) => changes++;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.UpdateSettingsAsync(settings =>
            {
                settings.Theme = ThemeMode.Dark;
                throw new InvalidOperationException("Patch failed.");
            }).WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken));

        using var reloaded = fixture.Reload();
        Assert.Equal(ThemeMode.System, fixture.Service.Settings.Theme);
        Assert.Equal(ThemeMode.System, reloaded.Settings.Theme);
        Assert.Equal(0, changes);
    }

    private sealed class ConfigurationFixture : IDisposable
    {
        private readonly string _directory = CreateTemporaryDirectory();

        public ConfigurationFixture()
        {
            File.WriteAllText(
                Path.Combine(_directory, "appsettings.json"),
                "{\"Aprs\":{\"Callsign\":\"N0CALL\"}}");
            Service = Reload();
        }

        public ConfigurationService Service { get; }

        public ConfigurationService Reload() =>
            new(new TestAppDataDirProvider(_directory), NullLogger<ConfigurationService>.Instance);

        public void Dispose()
        {
            Service.Dispose();
            Directory.Delete(_directory, recursive: true);
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

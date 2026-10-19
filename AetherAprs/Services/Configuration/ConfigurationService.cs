// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration.Settings;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Configuration;

public class ConfigurationService : IConfigurationService, IDisposable
{
    private readonly IAppDataDirProviderService _appDataDirProvider;
    private readonly ILogger<ConfigurationService> _logger;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _settingsLock.Dispose();
    }

    private static readonly string _appSettingsFileName = "appsettings.json";
#if DEBUG
    private static readonly string _appSettingsDevelopmentFileName = "appsettings.Development.json";
#endif

    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new SymbolTableJsonConverter(),
            new SymbolCodeJsonConverter(),
            new JsonStringEnumConverter()
        }
    };

    /// <summary>
    /// Gets the application settings.
    /// WARNING: Direct mutation of Settings properties requires calling SaveSettingsAsync() 
    /// to persist changes. Consider using UpdateSettingsAsync for automatic persistence.
    /// Use UpdateSettingsAsync for thread-safe read-modify-write cycles.
    /// </summary>
    public AppSettings Settings { get; }

    public ConfigurationService(
        IAppDataDirProviderService appDataDirProvider,
        ILogger<ConfigurationService> logger)
    {
        _appDataDirProvider = appDataDirProvider;
        _logger = logger;

        var configDirectory = _appDataDirProvider.GetAppDataDirectory();

        IConfiguration configuration;
        try
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(configDirectory)
                .AddJsonFile(
                    _appSettingsFileName,
                    optional: false,
                    reloadOnChange: false);

#if DEBUG
            builder.AddJsonFile(
                _appSettingsDevelopmentFileName,
                optional: true,
                reloadOnChange: false);
#endif

            configuration = builder.Build();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // A malformed or semantically invalid appsettings.json must not make
            // the app unstartable: quarantine the corrupt file and fall back to
            // built-in defaults (H3 fix).
            _logger.LogError(
                ex,
                "Failed to load {FileName}; quarantining it and using default settings.",
                _appSettingsFileName);

            QuarantineCorruptConfigFile(configDirectory);
            configuration = new ConfigurationBuilder()
                .SetBasePath(configDirectory)
                .Build();
        }

        Settings = new AppSettings();
        var normalizedValues = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var pair in configuration.AsEnumerable())
        {
            var value = pair.Value;
            string? normalizedPairValue;
            if (value != null
                && (pair.Key.Equals(
                    "Aprs:SymbolTableCharacter",
                    StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals(
                        "Aprs:SymbolCodeCharacter",
                        StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals(
                        "Aprs:SymbolOverlayCharacter",
                        StringComparison.OrdinalIgnoreCase)
                    // Legacy keys for migration
                    || pair.Key.Equals(
                        "Aprs:DefaultSymbolTableCharacter",
                        StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals(
                        "Aprs:DefaultSymbolCodeCharacter",
                        StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals(
                        "Aprs:DefaultSymbolOverlayCharacter",
                        StringComparison.OrdinalIgnoreCase)))
            {
                var normalizedKey = pair.Key switch
                {
                    _ when pair.Key.Equals(
                        "Aprs:SymbolTableCharacter",
                        StringComparison.OrdinalIgnoreCase)
                        || pair.Key.Equals(
                            "Aprs:DefaultSymbolTableCharacter",
                            StringComparison.OrdinalIgnoreCase)
                        => "Aprs:SymbolTable",
                    _ when pair.Key.Equals(
                        "Aprs:SymbolCodeCharacter",
                        StringComparison.OrdinalIgnoreCase)
                        || pair.Key.Equals(
                            "Aprs:DefaultSymbolCodeCharacter",
                            StringComparison.OrdinalIgnoreCase)
                        => "Aprs:SymbolCode",
                    _ => "Aprs:SymbolOverlay"
                };

                try
                {
                    normalizedPairValue = pair.Key.Contains("SymbolTable", StringComparison.OrdinalIgnoreCase)
                        ? value switch
                        {
                            "/" => SymbolTable.Primary.ToString(),
                            "\\" => SymbolTable.Alternate.ToString(),
                            _ => throw new InvalidOperationException(
                                $"Invalid APRS symbol table value '{value}'.")
                        }
                        : value.Length == 1
                            ? value[0].ToSymbolCode().ToString()
                            : throw new InvalidOperationException(
                                $"Invalid APRS symbol value '{value}'.");
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
                {
                    _logger.LogWarning(
                        ex,
                        "Invalid APRS symbol value '{Value}' for key {Key}; using default.",
                        value,
                        pair.Key);
                    continue;
                }

                normalizedValues[normalizedKey] = normalizedPairValue;
                continue;
            }

            normalizedValues[pair.Key] = value;
        }

        try
        {
            new ConfigurationBuilder()
                .AddInMemoryCollection(normalizedValues)
                .Build()
                .Bind(Settings);
        }
        catch (Exception ex)
        {
            // Throwing setters / unrecognized enum names during binding must not
            // prevent startup: fall back to defaults for the affected values.
            _logger.LogError(
                ex,
                "Failed to bind appsettings values; some settings may use defaults.");
        }
    }

    /// <summary>
    /// Renames a corrupt appsettings file out of the way so a subsequent save
    /// can write a fresh one, preserving the corrupt data for diagnosis.
    /// </summary>
    private void QuarantineCorruptConfigFile(string configDirectory)
    {
        try
        {
            var corruptPath = Path.Combine(configDirectory, _appSettingsFileName);
            if (!File.Exists(corruptPath))
            {
                return;
            }

            var quarantinedPath = Path.Combine(
                configDirectory,
                $"{_appSettingsFileName}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
            File.Move(corruptPath, quarantinedPath);
            _logger.LogWarning(
                "Corrupt config file moved to {QuarantinedPath}.",
                quarantinedPath);
        }
        catch (IOException ioEx)
        {
            _logger.LogError(ioEx, "Failed to quarantine corrupt config file.");
        }
    }

    public async Task SaveSettingsAsync()
    {
        var configDirectory = _appDataDirProvider.GetAppDataDirectory();

#if DEBUG
        var filePath = Path.Combine(
            configDirectory,
            _appSettingsDevelopmentFileName);
#else
        var filePath = Path.Combine(
            configDirectory,
            _appSettingsFileName);
#endif

        await _settingsLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(
                Settings,
                _jsonSerializerOptions);

            // Atomic write: serialize to a temp file, then atomically replace
            // the live config so a crash mid-write cannot corrupt it (H4 fix).
            var tempFilePath = filePath + ".tmp";
            await File.WriteAllTextAsync(tempFilePath, json);
            File.Move(tempFilePath, filePath, overwrite: true);
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <summary>
    /// Updates settings using the provided action and saves them atomically.
    /// This method is preferred over direct Settings mutation for ensuring changes are persisted.
    /// </summary>
    /// <param name="updateAction">Action that modifies the settings.</param>
    public async Task UpdateSettingsAsync(Action<AppSettings> updateAction)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        await _settingsLock.WaitAsync();
        try
        {
            updateAction(Settings);
            await SaveLockedAsync();
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <summary>
    /// Persists the current settings. Caller must hold <see cref="_settingsLock"/>.
    /// </summary>
    private async Task SaveLockedAsync()
    {
        var configDirectory = _appDataDirProvider.GetAppDataDirectory();

#if DEBUG
        var filePath = Path.Combine(
            configDirectory,
            _appSettingsDevelopmentFileName);
#else
        var filePath = Path.Combine(
            configDirectory,
            _appSettingsFileName);
#endif

        var json = JsonSerializer.Serialize(
            Settings,
            _jsonSerializerOptions);

        var tempFilePath = filePath + ".tmp";
        await File.WriteAllTextAsync(tempFilePath, json);
        File.Move(tempFilePath, filePath, overwrite: true);
    }

    /// <summary>
    /// Returns a thread-safe snapshot of the current settings.
    /// This is a deep clone — changes to the returned object do not affect the live settings.
    /// Use this method when you need consistent multi-property reads (e.g., callsign + SSID together).
    /// </summary>
    public AppSettings GetSettingsSnapshot()
    {
        _settingsLock.Wait();
        try
        {
            // Manual deep clone because AprsSettings has validated setters
            var snapshot = new AppSettings
            {
                Logging = Settings.Logging, // LoggerFilterOptions is framework type, reference is OK
                Aprs = new AprsSettings
                {
                    Callsign = Settings.Aprs.Callsign,
                    Ssid = Settings.Aprs.Ssid,
                    SymbolTable = Settings.Aprs.SymbolTable,
                    SymbolCode = Settings.Aprs.SymbolCode,
                    SymbolOverlay = Settings.Aprs.SymbolOverlay,
                    PacketRetentionDays = Settings.Aprs.PacketRetentionDays,
                    DisplayTimeRange = Settings.Aprs.DisplayTimeRange,
                    CustomDisplayTimeRangeHours = Settings.Aprs.CustomDisplayTimeRangeHours,
                    MessageMaxRetries = Settings.Aprs.MessageMaxRetries,
                    MessageRetryTimeoutSeconds = Settings.Aprs.MessageRetryTimeoutSeconds,
                    AutoAcknowledgeMessages = Settings.Aprs.AutoAcknowledgeMessages,
                    EnableDigipeater = Settings.Aprs.EnableDigipeater,
                    DigipeaterInsertCallsign = Settings.Aprs.DigipeaterInsertCallsign,
                    DigipeaterRespondToWide1 = Settings.Aprs.DigipeaterRespondToWide1,
                    DigipeaterRespondToWide2 = Settings.Aprs.DigipeaterRespondToWide2,
                    EnableAprsIsToRfGate = Settings.Aprs.EnableAprsIsToRfGate,
                    EnableRfToAprsIsGate = Settings.Aprs.EnableRfToAprsIsGate,
                    BeaconComment = Settings.Aprs.BeaconComment,
                    DigipeaterPath = Settings.Aprs.DigipeaterPath
                }
            };
            return snapshot;
        }
        finally
        {
            _settingsLock.Release();
        }
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration.Settings;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Platform;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AetherAprs.Services.Configuration;

public class ConfigurationService : IConfigurationService
{
    private readonly IAppDataDirProviderService _appDataDirProvider;

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
    /// This property is NOT thread-safe for concurrent modifications.
    /// </summary>
    public AppSettings Settings { get; }

    public ConfigurationService(IAppDataDirProviderService appDataDirProvider)
    {
        _appDataDirProvider = appDataDirProvider;

        var configDirectory = _appDataDirProvider.GetAppDataDirectory();

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

        var configuration = builder.Build();

        Settings = new AppSettings();
        var normalizedValues = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var pair in configuration.AsEnumerable())
        {
            var value = pair.Value;
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

                normalizedValues[normalizedKey] =
                    pair.Key.Contains("SymbolTable", StringComparison.OrdinalIgnoreCase)
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
                continue;
            }

            normalizedValues[pair.Key] = value;
        }

        new ConfigurationBuilder()
            .AddInMemoryCollection(normalizedValues)
            .Build()
            .Bind(Settings);
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

        var json = JsonSerializer.Serialize(
            Settings,
            _jsonSerializerOptions);

        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Updates settings using the provided action and saves them atomically.
    /// This method is preferred over direct Settings mutation for ensuring changes are persisted.
    /// </summary>
    /// <param name="updateAction">Action that modifies the settings.</param>
    public async Task UpdateSettingsAsync(Action<AppSettings> updateAction)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        updateAction(Settings);
        await SaveSettingsAsync();
    }
}

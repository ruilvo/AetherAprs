// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Configuration;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AetherAprs.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly IAppDataDirProviderService _appDataDirProvider;

    private static readonly string _appSettingsFileName = "appsettings.json";
    private static readonly string _appSettingsDevelopmentFileName = "appsettings.Development.json";

    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
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
        configuration.Bind(Settings);

        // ConfigurationBinder cannot materialize JsonPolymorphic TypeSettings/Transport.
        // Re-load Ports with System.Text.Json when a config file contains a Ports array.
        RepairPortsFromJson(configDirectory);
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

    private void RepairPortsFromJson(string configDirectory)
    {
#if DEBUG
        string[] candidates = [_appSettingsDevelopmentFileName, _appSettingsFileName];
#else
        string[] candidates = [_appSettingsFileName];
#endif

        foreach (var fileName in candidates)
        {
            var path = Path.Combine(configDirectory, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!TryGetPortsProperty(document.RootElement, out var portsElement))
            {
                continue;
            }

            var ports = JsonSerializer.Deserialize<List<PortConfig>>(
                portsElement.GetRawText(),
                _jsonSerializerOptions);

            if (ports is not null)
            {
                Settings.Ports = ports;
            }

            return;
        }
    }

    private static bool TryGetPortsProperty(JsonElement root, out JsonElement portsElement)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "Ports", StringComparison.OrdinalIgnoreCase))
            {
                portsElement = property.Value;
                return true;
            }
        }

        portsElement = default;
        return false;
    }
}
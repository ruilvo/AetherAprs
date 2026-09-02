// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Configuration;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace AetherAprs.Services;

public interface IConfigurationService
{
    /// <summary>
    /// Gets the strongly-typed application settings.
    /// </summary>
    AppSettings Settings { get; }

    /// <summary>
    /// Saves the current settings to the configuration file.
    /// </summary>
    Task SaveSettingsAsync();
}

public class ConfigurationService : IConfigurationService
{
    private readonly IAppDataDirProviderService _appDataDirProvider;

    private static readonly string _appSettingsFileName = "appsettings.json";
    private static readonly string _appSettingsDevelopmentFileName = "appsettings.Development.json";

    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true
    };

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
}

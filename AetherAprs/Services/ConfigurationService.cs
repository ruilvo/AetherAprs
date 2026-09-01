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
    /// Gets the configuration root for advanced scenarios.
    /// </summary>
    IConfiguration Configuration { get; }

    /// <summary>
    /// Saves the current settings to the configuration file.
    /// </summary>
    Task SaveSettingsAsync();
}

public class ConfigurationService : IConfigurationService
{
    private readonly IAppDataDirProviderService _appDataDirProvider;
    private static readonly string appSettingsFileName = "appsettings.json";
    private static readonly string appSettingsDevelopmentFileName = "appsettings.Development.json";
    private static readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Settings { get; }
    public IConfiguration Configuration { get; }

    public ConfigurationService(IAppDataDirProviderService appDataDirProvider)
    {
        _appDataDirProvider = appDataDirProvider;

        var configDirectory = _appDataDirProvider.GetAppDataDirectory();
        var builder = new ConfigurationBuilder()
            .SetBasePath(configDirectory)
            .AddJsonFile(appSettingsFileName, optional: false, reloadOnChange: false);

#if DEBUG
        // Only load environment-specific configuration in DEBUG builds
        builder.AddJsonFile(appSettingsDevelopmentFileName, optional: true, reloadOnChange: false);
#endif

        Configuration = builder.Build();

        // Bind the configuration to strongly-typed settings
        Settings = new AppSettings();
        Configuration.Bind(Settings);
    }

    public async Task SaveSettingsAsync()
    {
        var configDirectory = _appDataDirProvider.GetAppDataDirectory();

#if DEBUG
        // In DEBUG builds, save to Development file to keep base config clean
        var filePath = Path.Combine(configDirectory, appSettingsDevelopmentFileName);
#else
        // In RELEASE builds, save to base file
        var filePath = Path.Combine(configDirectory, appSettingsFileName);
#endif

        var json = JsonSerializer.Serialize(Settings, jsonSerializerOptions);

        await File.WriteAllTextAsync(filePath, json);
    }
}

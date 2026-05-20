// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace AetherAprs.Services.Configuration;

// TODO: This doesn't work for Android/iOS because for those we need the AppData folder,
// but for now we can just use the appsettings.json file in the app directory and
// not worry about saving settings on mobile platforms.
public class ConfigurationService : IConfigurationService
{
    private readonly IConfiguration _configuration;
    private readonly string _settingsFilePath;
    public AppSettings Settings { get; private set; }

    public ConfigurationService()
    {
        var environment = Environment.GetEnvironmentVariable("NETCORE_ENVIRONMENT") ?? "Production";

        var basePath = AppContext.BaseDirectory;

        var envFileName = $"appsettings.{environment}.json";
        var envFilePath = Path.Combine(basePath, envFileName);
        var defaultFileName = "appsettings.json";
        var defaultFilePath = Path.Combine(basePath, defaultFileName);

        _settingsFilePath = File.Exists(envFilePath) ? envFilePath : defaultFilePath;

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile(defaultFileName, optional: false, reloadOnChange: false)
            .AddJsonFile(envFileName, optional: true, reloadOnChange: false);

        _configuration = builder.Build();
        Settings = _configuration.GetSection("AppSettings").Get<AppSettings>() ?? new AppSettings();
    }

    public void SaveSettings(AppSettings newSettings)
    {
        Settings = newSettings;

        var jsonText = File.Exists(_settingsFilePath) ? File.ReadAllText(_settingsFilePath) : "{}";
        var root = JsonNode.Parse(jsonText) as JsonObject ?? new JsonObject();

        root["AppSettings"] = JsonSerializer.SerializeToNode(Settings);

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_settingsFilePath, root.ToJsonString(options));
    }
}

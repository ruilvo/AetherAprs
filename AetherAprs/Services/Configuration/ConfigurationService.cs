// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace AetherAprs.Services.Configuration;

/// <summary>
/// Loads and persists application settings from JSON configuration files.
/// On mobile platforms, settings are stored in the app's local data folder.
/// On desktop, settings are stored alongside the application binary.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private const string EnvironmentVariableName = "DOTNET_ENVIRONMENT";
    private const string DefaultEnvironment = "Production";
    private const string AppSettingsSection = "AppSettings";
    private const string DefaultFileName = "appsettings.json";

    private readonly string _settingsFilePath;

    /// <inheritdoc/>
    public AppSettings Settings { get; private set; }

    public ConfigurationService()
    {
        var environment = Environment.GetEnvironmentVariable(EnvironmentVariableName) ?? DefaultEnvironment;

        var basePath = GetSettingsBasePath();
        var envFileName = $"appsettings.{environment}.json";

        _settingsFilePath = ResolveSettingsFilePath(basePath, envFileName);

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile(DefaultFileName, optional: true, reloadOnChange: false)
            .AddJsonFile(envFileName, optional: true, reloadOnChange: false);

        var configuration = builder.Build();
        Settings = configuration.GetSection(AppSettingsSection).Get<AppSettings>() ?? new AppSettings();

        try
        {
            Settings.Validate();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Fall back to defaults if stored settings are invalid
            Settings = new AppSettings();
        }
    }

    /// <inheritdoc/>
    public bool SaveSettings(AppSettings newSettings)
    {
        ArgumentNullException.ThrowIfNull(newSettings);

        try
        {
            newSettings.Validate();
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var jsonText = File.Exists(_settingsFilePath) ? File.ReadAllText(_settingsFilePath) : "{}";
            var root = JsonNode.Parse(jsonText) as JsonObject ?? new JsonObject();

            root[AppSettingsSection] = JsonSerializer.SerializeToNode(newSettings);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_settingsFilePath, root.ToJsonString(options));

            Settings = newSettings;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns the base path for settings files, accounting for mobile platforms
    /// where the app directory is read-only.
    /// </summary>
    private static string GetSettingsBasePath()
    {
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var settingsDir = Path.Combine(appData, "AetherAprs");

            if (!Directory.Exists(settingsDir))
            {
                Directory.CreateDirectory(settingsDir);
            }

            // Seed default settings file if it doesn't exist on mobile
            var mobileSettingsPath = Path.Combine(settingsDir, DefaultFileName);
            if (!File.Exists(mobileSettingsPath))
            {
                File.WriteAllText(mobileSettingsPath, "{}");
            }

            return settingsDir;
        }

        return AppContext.BaseDirectory;
    }

    private static string ResolveSettingsFilePath(string basePath, string envFileName)
    {
        var envFilePath = Path.Combine(basePath, envFileName);
        var defaultFilePath = Path.Combine(basePath, DefaultFileName);

        return File.Exists(envFilePath) ? envFilePath : defaultFilePath;
    }
}

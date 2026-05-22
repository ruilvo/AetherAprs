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
/// Base implementation for loading and persisting application settings from JSON configuration files.
/// Platform-specific services provide the writable settings base path.
/// </summary>
public abstract class ConfigurationService : IConfigurationService
{
    private const string EnvironmentVariableName = "DOTNET_ENVIRONMENT";
    private const string DefaultEnvironment = "Production";
    private const string AppSettingsSection = "AppSettings";
    protected const string DefaultFileName = "appsettings.json";

    private readonly string _settingsFilePath;

    /// <inheritdoc/>
    public AppSettings Settings { get; private set; }

    protected ConfigurationService()
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
    /// Returns the platform-specific base path for settings files.
    /// </summary>
    protected abstract string GetSettingsBasePath();

    private static string ResolveSettingsFilePath(string basePath, string envFileName)
    {
        var envFilePath = Path.Combine(basePath, envFileName);
        var defaultFilePath = Path.Combine(basePath, DefaultFileName);

        return File.Exists(envFilePath) ? envFilePath : defaultFilePath;
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Models;
using AetherAprs.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace AetherAprs.Data;

/// <summary>
/// Applies EF migrations and imports legacy ports from appsettings.json once.
/// </summary>
public sealed class AppSavedDataInitializer(
    IDbContextFactory<AppDbContext> dbContextFactory,
    IAppDataDirProviderService appDataDirProvider,
    ILogger<AppSavedDataInitializer> logger)
{
    private static readonly string _appSettingsFileName = "appsettings.json";
    private static readonly string _appSettingsDevelopmentFileName = "appsettings.Development.json";
    private readonly Lock _gate = new();
    private bool _initialized;

    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }

            using var db = dbContextFactory.CreateDbContext();
            db.Database.Migrate();
            ImportLegacyPorts(db);
            SeedBeaconingDefaults(db);
            _initialized = true;
        }
    }

    private void ImportLegacyPorts(AppDbContext db)
    {
        if (db.Ports.Any())
        {
            StripLegacyPortsFromAppSettings();
            return;
        }

        var ports = ReadLegacyPorts();
        if (ports.Count == 0)
        {
            return;
        }

        foreach (var port in ports)
        {
            db.Ports.Add(PortRecordMapper.ToRecord(port));
        }

        db.SaveChanges();
        logger.LogInformation("Imported {PortCount} port(s) from appsettings into savedata.", ports.Count);
        StripLegacyPortsFromAppSettings();
    }

    private static void SeedBeaconingDefaults(AppDbContext db)
    {
        if (!db.BeaconConfigs.Any())
        {
            db.BeaconConfigs.AddRange(
                BeaconConfigMapper.ToRecord(BeaconConfig.CreateWalkPreset()),
                BeaconConfigMapper.ToRecord(BeaconConfig.CreateDrivePreset()),
                BeaconConfigMapper.ToRecord(BeaconConfig.CreateCustomPreset()));
        }

        if (!db.BeaconingState.Any())
        {
            db.BeaconingState.Add(new BeaconingStateRecord());
        }

        db.SaveChanges();
    }

    private List<PortConfig> ReadLegacyPorts()
    {
        var configDirectory = appDataDirProvider.GetAppDataDirectory();

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

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (!TryGetPortsProperty(document.RootElement, out var portsElement))
                {
                    continue;
                }

                var ports = JsonSerializer.Deserialize<List<PortConfig>>(
                    portsElement.GetRawText(),
                    SavedDataJson.Options);

                if (ports is { Count: > 0 })
                {
                    return ports;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read legacy ports from {Path}.", path);
            }
        }

        return [];
    }

    private void StripLegacyPortsFromAppSettings()
    {
        var configDirectory = appDataDirProvider.GetAppDataDirectory();
        string[] candidates = [_appSettingsFileName, _appSettingsDevelopmentFileName];

        foreach (var fileName in candidates)
        {
            var path = Path.Combine(configDirectory, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject node)
                {
                    continue;
                }

                string? portsKey = null;
                foreach (var property in node)
                {
                    if (string.Equals(property.Key, "Ports", StringComparison.OrdinalIgnoreCase))
                    {
                        portsKey = property.Key;
                        break;
                    }
                }

                if (portsKey is null)
                {
                    continue;
                }

                node.Remove(portsKey);
                File.WriteAllText(
                    path,
                    node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to strip legacy Ports from {Path}.", path);
            }
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

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration.Settings;
using AetherAprs.Configuration.Settings.Migrations;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Configuration;

public class ConfigurationService : IConfigurationService, IDisposable
{
    private readonly IAppDataDirProviderService _appDataDirProvider;
    private readonly ILogger<ConfigurationService> _logger;
    private readonly IConfiguration _configuration;
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

    /// <inheritdoc />
    public event EventHandler? SettingsChanged;

    public ConfigurationService(
        IAppDataDirProviderService appDataDirProvider,
        ILogger<ConfigurationService> logger)
    {
        _appDataDirProvider = appDataDirProvider;
        _logger = logger;

        var configDirectory = _appDataDirProvider.GetAppDataDirectory();

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

            _configuration = builder.Build();
        }
        catch (Exception ex) when (ex is JsonException
            or InvalidOperationException
            or FormatException
            or IOException
            or System.IO.InvalidDataException)
        {
            // A malformed or semantically invalid appsettings.json must not make
            // the app unstartable: quarantine the corrupt file and fall back to
            // built-in defaults (H3 fix). Microsoft.Extensions.Configuration's
            // JSON provider surfaces syntax errors as FormatException wrapped in
            // InvalidDataException (a SystemException, not an IOException), so
            // those types must be handled explicitly.
            _logger.LogError(
                ex,
                "Failed to load {FileName}; quarantining it and using default settings.",
                _appSettingsFileName);

            QuarantineCorruptConfigFile(configDirectory);
            _configuration = new ConfigurationBuilder()
                .SetBasePath(configDirectory)
                .Build();
        }

        Settings = new AppSettings();
        var normalizedValues = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _configuration.AsEnumerable())
        {
            // Skip the version marker here: it is read separately below so the
            // document's own declared version, not the default, drives migration.
            if (pair.Key.Equals(
                SettingsSchema.VersionPropertyName,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            normalizedValues[pair.Key] = pair.Value;
        }

        var documentVersion = ReadDocumentVersion();
        LegacySymbolKeyNormalizer.Normalize(normalizedValues, _logger);
        var migratedVersion = SettingsMigrationPipeline.Migrate(
            normalizedValues,
            documentVersion,
            SettingsSchema.CurrentVersion,
            _logger);

        if (documentVersion > SettingsSchema.CurrentVersion)
        {
            _logger.LogWarning(
                "Settings file declares schema version {DocumentVersion}, which is newer than this build ({CurrentVersion}). It will be loaded as-is and backed up before the next save.",
                documentVersion,
                SettingsSchema.CurrentVersion);
        }

        if (migratedVersion < SettingsSchema.CurrentVersion)
        {
            _logger.LogWarning(
                "Settings could not be fully migrated (now at version {Version}, expected {TargetVersion}).",
                migratedVersion,
                SettingsSchema.CurrentVersion);
        }

        // Preserve a copy of the file as it was before any migration so a bad
        // upgrade can be diagnosed or rolled back.
        if (documentVersion != SettingsSchema.CurrentVersion)
        {
            BackupSettingsFile(configDirectory, documentVersion);
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

        Settings.SchemaVersion = SettingsSchema.CurrentVersion;

        SanitizeMessageTemplates();
    }

    /// <summary>
    /// Reads the schema version declared by the settings document, or
    /// <see cref="SettingsSchema.LegacyUnversionedVersion"/> when the file
    /// predates schema versioning or declares an unusable value.
    /// </summary>
    private int ReadDocumentVersion()
    {
        var raw = _configuration[SettingsSchema.VersionPropertyName];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return SettingsSchema.LegacyUnversionedVersion;
        }

        if (int.TryParse(raw, out var version) && version >= SettingsSchema.LegacyUnversionedVersion)
        {
            return version;
        }

        _logger.LogWarning(
            "Ignoring invalid settings schema version '{RawVersion}'.",
            raw);
        return SettingsSchema.LegacyUnversionedVersion;
    }

    /// <summary>
    /// Copies the settings file aside before an upgrade rewrites it, so a
    /// migration problem cannot strand the user without their original values.
    /// At most one backup is kept per source version.
    /// </summary>
    private void BackupSettingsFile(string configDirectory, int fromVersion)
    {
        try
        {
            var sourcePath = Path.Combine(configDirectory, _appSettingsFileName);
            if (!File.Exists(sourcePath))
            {
                return;
            }

            // The unversioned source file can be loaded repeatedly (saves may go
            // to an overlay file), so only preserve its contents once.
            var existingBackups = Directory.GetFiles(
                configDirectory,
                $"{_appSettingsFileName}.v{fromVersion}.*.bak");
            if (existingBackups.Length > 0)
            {
                return;
            }

            var backupPath = Path.Combine(
                configDirectory,
                $"{_appSettingsFileName}.v{fromVersion}.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.bak");
            File.Copy(sourcePath, backupPath, overwrite: false);
            _logger.LogInformation(
                "Backed up settings schema version {FromVersion} to {BackupPath}.",
                fromVersion,
                backupPath);
        }
        catch (IOException ioEx)
        {
            _logger.LogError(ioEx, "Failed to back up the settings file before migration.");
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

    /// <summary>
    /// Gets the path of the settings file that saves write to.
    /// </summary>
    private string GetSettingsFilePath()
    {
        var configDirectory = _appDataDirProvider.GetAppDataDirectory();

#if DEBUG
        return Path.Combine(
            configDirectory,
            _appSettingsDevelopmentFileName);
#else
        return Path.Combine(
            configDirectory,
            _appSettingsFileName);
#endif
    }

    public async Task SaveSettingsAsync()
    {
        var filePath = GetSettingsFilePath();

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

        SettingsChanged?.Invoke(this, EventArgs.Empty);
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

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Persists the current settings. Caller must hold <see cref="_settingsLock"/>.
    /// </summary>
    private async Task SaveLockedAsync()
    {
        var filePath = GetSettingsFilePath();

        var json = JsonSerializer.Serialize(
            Settings,
            _jsonSerializerOptions);

        var tempFilePath = filePath + ".tmp";
        await File.WriteAllTextAsync(tempFilePath, json);
        File.Move(tempFilePath, filePath, overwrite: true);
    }

    /// <summary>
    /// Persists the current settings synchronously, using the same atomic
    /// temp-file write as <see cref="SaveSettingsAsync"/>.
    /// Used during construction, where an async save cannot be awaited.
    /// </summary>
    private void SaveSettingsSync()
    {
        var filePath = GetSettingsFilePath();

        _settingsLock.Wait();
        try
        {
            var json = JsonSerializer.Serialize(
                Settings,
                _jsonSerializerOptions);

            var tempFilePath = filePath + ".tmp";
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, filePath, overwrite: true);
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <summary>
    /// Drops message templates that are unusable: a null/whitespace name, a
    /// destination that is not a valid APRS address, or a name that duplicates an
    /// earlier template (case-insensitive). First occurrences are kept in
    /// their original order.
    /// </summary>
    private void SanitizeMessageTemplates()
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sanitized = new List<MessageTemplate>();

        foreach (var template in Settings.MessageTemplates.Templates)
        {
            if (string.IsNullOrWhiteSpace(template.Name))
            {
                _logger.LogWarning(
                    "Dropping message template with empty name.");
                continue;
            }

            if (!AprsAddress.TryParse(template.Destination, out _))
            {
                _logger.LogWarning(
                    "Dropping message template {TemplateName} with invalid destination {Destination}.",
                    template.Name,
                    template.Destination);
                continue;
            }

            if (!seenNames.Add(template.Name))
            {
                _logger.LogWarning(
                    "Dropping duplicate message template {TemplateName}.",
                    template.Name);
                continue;
            }

            sanitized.Add(template);
        }

        Settings.MessageTemplates.Templates = sanitized;
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
                    PacketRetentionMaxRows = Settings.Aprs.PacketRetentionMaxRows,
                    DisplayTimeRange = Settings.Aprs.DisplayTimeRange,
                    CustomDisplayTimeRangeHours = Settings.Aprs.CustomDisplayTimeRangeHours,
                    LoadHistoryOnStartup = Settings.Aprs.LoadHistoryOnStartup,
                    MessageMaxRetries = Settings.Aprs.MessageMaxRetries,
                    MessageRetryTimeoutSeconds = Settings.Aprs.MessageRetryTimeoutSeconds,
                    AutoAcknowledgeMessages = Settings.Aprs.AutoAcknowledgeMessages,
                    EnableDigipeater = Settings.Aprs.EnableDigipeater,
                    DigipeaterInsertCallsign = Settings.Aprs.DigipeaterInsertCallsign,
                    DigipeaterRespondToWide1 = Settings.Aprs.DigipeaterRespondToWide1,
                    DigipeaterRespondToWide2 = Settings.Aprs.DigipeaterRespondToWide2,
                    DigipeaterRespondToTrace = Settings.Aprs.DigipeaterRespondToTrace,
                    EnableAprsIsToRfGate = Settings.Aprs.EnableAprsIsToRfGate,
                    EnableRfToAprsIsGate = Settings.Aprs.EnableRfToAprsIsGate,
                    BeaconComment = Settings.Aprs.BeaconComment,
                    PositionFormat = Settings.Aprs.PositionFormat,
                    DigipeaterPath = Settings.Aprs.DigipeaterPath
                },
                MessageTemplates = new MessageTemplatesSettings
                {
                    Templates = Settings.MessageTemplates.Templates
                        .Select(template => new MessageTemplate
                        {
                            Name = template.Name,
                            Destination = template.Destination,
                            Body = template.Body,
                            FieldDefaults = new Dictionary<string, string>(template.FieldDefaults)
                        })
                        .ToList()
                },
                Map = new MapSettings
                {
                    TileSource = Settings.Map.TileSource,
                    MbTilesFilePath = Settings.Map.MbTilesFilePath
                },
                Theme = Settings.Theme
            };
            return snapshot;
        }
        finally
        {
            _settingsLock.Release();
        }
    }
}

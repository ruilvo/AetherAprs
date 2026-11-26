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
    private AppSettings _settings = new();

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
    /// Gets the current settings reference. Treat it as read-only and do not
    /// retain it across updates. Direct mutation is supported for legacy callers only.
    /// </summary>
    public AppSettings Settings => Volatile.Read(ref _settings);

    /// <summary>
    /// Whether the settings file was written by a newer build and must not be
    /// overwritten. When <see langword="true"/>, every save or update path is
    /// refused so a downgrade cannot destroy settings or their schema version.
    /// </summary>
    public bool IsReadOnly { get; private set; }

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

        var isNewerDocument = documentVersion > SettingsSchema.CurrentVersion;
        var migrationSucceeded = documentVersion < SettingsSchema.CurrentVersion
            && migratedVersion == SettingsSchema.CurrentVersion;

        if (isNewerDocument)
        {
            IsReadOnly = true;
            _logger.LogWarning(
                "Settings file declares schema version {DocumentVersion}, which is newer than this build ({CurrentVersion}). Settings are loaded read-only; saves are refused so the newer file is not overwritten.",
                documentVersion,
                SettingsSchema.CurrentVersion);
        }
        else if (migrationSucceeded)
        {
            _logger.LogInformation(
                "Migrated settings from schema version {DocumentVersion} to {CurrentVersion}.",
                documentVersion,
                SettingsSchema.CurrentVersion);
        }
        else if (documentVersion < SettingsSchema.CurrentVersion)
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

        BindSettings(normalizedValues);

        // Only claim the current version when this build actually produced it.
        // For newer documents the document's own version is kept, which is the
        // signal that a downgrade must not overwrite the file.
        if (migrationSucceeded)
        {
            Settings.SchemaVersion = SettingsSchema.CurrentVersion;
        }
        else if (isNewerDocument)
        {
            Settings.SchemaVersion = documentVersion;
        }
        else
        {
            Settings.SchemaVersion = migratedVersion;
        }

        SanitizeMessageTemplates();

        // Make the on-disk state match memory after a successful upgrade, so a
        // crash before the next save cannot leave a half-migrated file behind.
        if (migrationSucceeded)
        {
            PersistMigratedSettings();
        }
    }

    /// <summary>
    /// Binds the flattened settings values one property at a time so one
    /// invalid value (for example a rejected validated setter) is logged and
    /// skipped without discarding the values that follow it.
    /// </summary>
    private void BindSettings(IDictionary<string, string?> values)
    {
        // The template list is bound as a whole section: binding it key-by-key
        // would rebuild the list on every value. Its property setters are
        // unvalidated strings, so a failure there cannot leak into another
        // section. Every scalar property, including Logging, binds individually.
        BindSection(values, "MessageTemplates");

        foreach (var pair in values)
        {
            if (IsInSection(pair.Key, "MessageTemplates"))
            {
                continue;
            }

            BindSingleValue(pair.Key, pair.Value);
        }
    }

    private void BindSection(IDictionary<string, string?> values, string section)
    {
        var sectionValues = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var pair in values)
        {
            if (IsInSection(pair.Key, section))
            {
                sectionValues[pair.Key] = pair.Value;
            }
        }

        if (sectionValues.Count == 0)
        {
            return;
        }

        try
        {
            new ConfigurationBuilder()
                .AddInMemoryCollection(sectionValues)
                .Build()
                .Bind(Settings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Ignoring invalid values in settings section {Section}; affected properties use their defaults.",
                section);
        }
    }

    private void BindSingleValue(string key, string? value)
    {
        var singleValue = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase)
        {
            [key] = value,
        };

        try
        {
            new ConfigurationBuilder()
                .AddInMemoryCollection(singleValue)
                .Build()
                .Bind(Settings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Ignoring invalid settings value for {Key}: {Value}.",
                key,
                value);
        }
    }

    private static bool IsInSection(string key, string section)
    {
        return key.Equals(section, StringComparison.OrdinalIgnoreCase)
            || key.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Persists the newly migrated settings during construction, where the
    /// asynchronous save path cannot be awaited.
    /// </summary>
    private void PersistMigratedSettings()
    {
        try
        {
            var filePath = GetSettingsFilePath();
            File.WriteAllText(filePath + ".tmp", JsonSerializer.Serialize(Settings, _jsonSerializerOptions));
            File.Move(filePath + ".tmp", filePath, overwrite: true);
            _logger.LogInformation(
                "Persisted settings at schema version {Version}.",
                SettingsSchema.CurrentVersion);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            _logger.LogError(
                ex,
                "Failed to persist migrated settings; the in-memory settings remain migrated.");
        }
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
        if (IsReadOnly)
        {
            LogReadOnlyWriteRefused(nameof(SaveSettingsAsync));
            return;
        }

        await _settingsLock.WaitAsync();
        try
        {
            await SaveJsonAsync(JsonSerializer.Serialize(Settings, _jsonSerializerOptions));
        }
        finally
        {
            _settingsLock.Release();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies a patch to a private draft and publishes the changes only after persistence succeeds.
    /// </summary>
    /// <param name="updateAction">Synchronous action that modifies the draft.</param>
    public async Task UpdateSettingsAsync(Action<AppSettings> updateAction)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        if (IsReadOnly)
        {
            LogReadOnlyWriteRefused(nameof(UpdateSettingsAsync));
            return;
        }

        await _settingsLock.WaitAsync();
        try
        {
            var draft = GetSettingsSnapshot();
            updateAction(draft);
            var json = JsonSerializer.Serialize(draft, _jsonSerializerOptions);
            // Never publish references supplied to or retained by the update callback.
            var committed = DeserializeSettings(json);
            await SaveJsonAsync(json);
            Volatile.Write(ref _settings, committed);
        }
        finally
        {
            _settingsLock.Release();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Persists the current settings with an atomic temp-file write.
    /// Caller must hold <see cref="_settingsLock"/>.
    /// </summary>
    private async Task SaveJsonAsync(string json)
    {
        var filePath = GetSettingsFilePath();

        // Atomic write: serialize to a temp file, then replace the live config
        // so a crash mid-write cannot corrupt it (H4 fix).
        var tempFilePath = filePath + ".tmp";
        await File.WriteAllTextAsync(tempFilePath, json).ConfigureAwait(false);
        File.Move(tempFilePath, filePath, overwrite: true);
    }

    private void LogReadOnlyWriteRefused(string operation)
    {
        _logger.LogWarning(
            "Refusing {Operation}: the settings file declares schema version {DocumentVersion}, which is newer than this build ({CurrentVersion}), so it is read-only.",
            operation,
            Settings.SchemaVersion,
            SettingsSchema.CurrentVersion);
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
    /// Returns a deep copy of the current settings without waiting for an in-flight update.
    /// Legacy callers must not mutate the live settings concurrently.
    /// Use this method when you need consistent multi-property reads (e.g., callsign + SSID together).
    /// </summary>
    public AppSettings GetSettingsSnapshot() =>
        DeserializeSettings(JsonSerializer.Serialize(Settings, _jsonSerializerOptions));

    private static AppSettings DeserializeSettings(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json, _jsonSerializerOptions)!;
}

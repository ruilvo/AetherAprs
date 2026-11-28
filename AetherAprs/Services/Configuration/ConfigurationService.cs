// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration.Settings;
using AetherAprs.Configuration.Settings.Documents;
using AetherAprs.Configuration.Settings.Migrations;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Platform;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Configuration;

/// <summary>
/// Loads, migrates and persists the application settings file, publishing an
/// immutable <see cref="AppSettings"/> snapshot after every successful write.
/// </summary>
/// <remarks>
/// Loading binds the flattened configuration values into a private
/// <see cref="AppSettingsDocument"/> and then converts it once into the
/// published snapshot, so no caller ever holds the object the binder mutates.
/// </remarks>
public class ConfigurationService : IConfigurationService, IDisposable
{
    private readonly IAppDataDirProviderService _appDataDirProvider;
    private readonly ILogger<ConfigurationService> _logger;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Serializes writes so two updates can never persist or publish in an
    /// order different from the one in which they were accepted.
    /// </summary>
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    private AppSettings _current = new();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _updateGate.Dispose();
    }

    private static readonly string _appSettingsFileName = "appsettings.json";
#if DEBUG
    private static readonly string _appSettingsDevelopmentFileName = "appsettings.Development.json";
#endif

    /// <summary>
    /// Name of the section the host logging configuration owns. The settings
    /// snapshot does not model it; a save preserves whatever the file holds.
    /// </summary>
    private const string LoggingSectionName = "Logging";

    /// <summary>
    /// Options for writing a parsed <see cref="JsonNode"/>. Only indentation
    /// applies: the node already carries its own structure and no type metadata
    /// is resolved.
    /// </summary>
    private static readonly JsonSerializerOptions _nodeWriterOptions = new()
    {
        WriteIndented = true
    };

    /// <inheritdoc />
    public AppSettings Current => Volatile.Read(ref _current);

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

        string configDirectory = _appDataDirProvider.GetAppDataDirectory();

        try
        {
            IConfigurationBuilder builder = new ConfigurationBuilder()
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

        Dictionary<string, string?> normalizedValues = [with(
            StringComparer.OrdinalIgnoreCase)];

        foreach (KeyValuePair<string, string?> pair in _configuration.AsEnumerable())
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

        int documentVersion = ReadDocumentVersion();
        LegacySymbolKeyNormalizer.Normalize(normalizedValues, _logger);
        int migratedVersion = SettingsMigrationPipeline.Migrate(
            normalizedValues,
            documentVersion,
            SettingsSchema.CurrentVersion,
            _logger);

        bool isNewerDocument = documentVersion > SettingsSchema.CurrentVersion;
        bool migrationSucceeded = documentVersion < SettingsSchema.CurrentVersion
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

        AppSettingsDocument document = new();
        BindSettings(document, normalizedValues);
        SanitizeMessageTemplates(document);

        // Only claim the current version when this build actually produced it.
        // For newer documents the document's own version is kept, which is the
        // signal that a downgrade must not overwrite the file.
        document.SchemaVersion = migrationSucceeded
            ? SettingsSchema.CurrentVersion
            : isNewerDocument
                ? documentVersion
                : migratedVersion;

        _current = document.ToSettings();

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
    private void BindSettings(AppSettingsDocument document, IDictionary<string, string?> values)
    {
        // The template list is bound as a whole section: binding it key-by-key
        // would rebuild the list on every value. Its property setters are
        // unvalidated strings, so a failure there cannot leak into another
        // section. The logging section is not modelled at all: the host logging
        // configuration owns it and a save preserves it verbatim.
        BindSection(document, values, "MessageTemplates");

        foreach (KeyValuePair<string, string?> pair in values)
        {
            if (IsInSection(pair.Key, "MessageTemplates") || IsInSection(pair.Key, LoggingSectionName))
            {
                continue;
            }

            BindSingleValue(document, pair.Key, pair.Value);
        }
    }

    private void BindSection(
        AppSettingsDocument document,
        IDictionary<string, string?> values,
        string section)
    {
        Dictionary<string, string?> sectionValues = [with(
            StringComparer.OrdinalIgnoreCase)];

        foreach (KeyValuePair<string, string?> pair in values)
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
                .Bind(document);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Ignoring invalid values in settings section {Section}; affected properties use their defaults.",
                section);
        }
    }

    private void BindSingleValue(AppSettingsDocument document, string key, string? value)
    {
        Dictionary<string, string?> singleValue = new(
            StringComparer.OrdinalIgnoreCase)
        {
            [key] = value,
        };

        try
        {
            new ConfigurationBuilder()
                .AddInMemoryCollection(singleValue)
                .Build()
                .Bind(document);
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

    private static bool IsInSection(string key, string section) => key.Equals(section, StringComparison.OrdinalIgnoreCase)
            || key.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Persists the newly migrated settings during construction, where the
    /// asynchronous save path cannot be awaited.
    /// </summary>
    private void PersistMigratedSettings()
    {
        try
        {
            string filePath = GetSettingsFilePath();
            File.WriteAllText(filePath + ".tmp", SerializeSettings(Current));
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
    /// Serializes a snapshot for the settings file and preserves the sections the
    /// snapshot does not model.
    /// </summary>
    /// <remarks>
    /// The metadata is source-generated, so writing the file works in a trimmed
    /// application. <c>Logging</c> belongs to the host logging configuration: the
    /// snapshot deliberately does not model it, so the node already on disk is
    /// carried over instead of being dropped by a settings save.
    /// </remarks>
    private string SerializeSettings(AppSettings settings)
    {
        JsonObject node = JsonSerializer.SerializeToNode(settings, SettingsJsonContext.Default.AppSettings)!.AsObject();

        if (ReadExistingSection(GetSettingsFilePath(), LoggingSectionName) is { } logging)
        {
            node[LoggingSectionName] = logging;
        }

        return node.ToJsonString(_nodeWriterOptions);
    }

    private JsonNode? ReadExistingSection(string filePath, string section)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            return JsonNode.Parse(File.ReadAllText(filePath))?[section]?.DeepClone();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            // A corrupt or unreadable file is the load path's problem. A save
            // simply has no section to preserve.
            _logger.LogWarning(
                exception,
                "Could not preserve the {Section} section of the settings file.",
                section);
            return null;
        }
    }

    /// <summary>
    /// Reads the schema version declared by the settings document, or
    /// <see cref="SettingsSchema.LegacyUnversionedVersion"/> when the file
    /// predates schema versioning or declares an unusable value.
    /// </summary>
    private int ReadDocumentVersion()
    {
        string? raw = _configuration[SettingsSchema.VersionPropertyName];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return SettingsSchema.LegacyUnversionedVersion;
        }

        if (int.TryParse(raw, out int version) && version >= SettingsSchema.LegacyUnversionedVersion)
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
            string sourcePath = Path.Combine(configDirectory, _appSettingsFileName);
            if (!File.Exists(sourcePath))
            {
                return;
            }

            // The unversioned source file can be loaded repeatedly (saves may go
            // to an overlay file), so only preserve its contents once.
            string[] existingBackups = Directory.GetFiles(
                configDirectory,
                $"{_appSettingsFileName}.v{fromVersion}.*.bak");
            if (existingBackups.Length > 0)
            {
                return;
            }

            string backupPath = Path.Combine(
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
            string corruptPath = Path.Combine(configDirectory, _appSettingsFileName);
            if (!File.Exists(corruptPath))
            {
                return;
            }

            string quarantinedPath = Path.Combine(
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
        string configDirectory = _appDataDirProvider.GetAppDataDirectory();

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

    /// <inheritdoc />
    public async Task<AppSettings> UpdateAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (IsReadOnly)
        {
            LogReadOnlyWriteRefused(nameof(UpdateAsync));
            return Current;
        }

        AppSettings updated;

        await _updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings candidate = update(Current)
                ?? throw new InvalidOperationException(
                    "A settings update must return a settings snapshot.");

            // Persist before publishing: a failed write leaves the previously
            // published snapshot in place, so memory never claims a state the
            // file does not hold.
            await SaveJsonAsync(
                SerializeSettings(candidate),
                cancellationToken).ConfigureAwait(false);

            Volatile.Write(ref _current, candidate);
            updated = candidate;
        }
        finally
        {
            _updateGate.Release();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return updated;
    }

    /// <summary>
    /// Persists the given JSON with an atomic temp-file write.
    /// Caller must hold <see cref="_updateGate"/>.
    /// </summary>
    private async Task SaveJsonAsync(string json, CancellationToken cancellationToken)
    {
        string filePath = GetSettingsFilePath();

        // Atomic write: serialize to a temp file, then replace the live config
        // so a crash mid-write cannot corrupt it (H4 fix).
        string tempFilePath = filePath + ".tmp";
        await File.WriteAllTextAsync(tempFilePath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempFilePath, filePath, overwrite: true);
    }

    private void LogReadOnlyWriteRefused(string operation) => _logger.LogWarning(
            "Refusing {Operation}: the settings file declares schema version {DocumentVersion}, which is newer than this build ({CurrentVersion}), so it is read-only.",
            operation,
            Current.SchemaVersion,
            SettingsSchema.CurrentVersion);

    /// <summary>
    /// Drops message templates that are unusable: a null/whitespace name, a
    /// destination that is not a valid APRS address, or a name that duplicates an
    /// earlier template (case-insensitive). First occurrences are kept in
    /// their original order.
    /// </summary>
    private void SanitizeMessageTemplates(AppSettingsDocument document)
    {
        HashSet<string> seenNames = [with(StringComparer.OrdinalIgnoreCase)];
        List<MessageTemplateDocument> sanitized = [];

        foreach (MessageTemplateDocument template in document.MessageTemplates.Templates)
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

        document.MessageTemplates.Templates = sanitized;
    }
}

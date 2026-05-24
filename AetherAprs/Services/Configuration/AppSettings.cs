// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Logging;
using System;
using System.Text.Json.Serialization;

namespace AetherAprs.Services.Configuration;

/// <summary>
/// Strongly-typed application settings persisted to <c>appsettings.json</c>.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Gets or sets logging-related settings.
    /// </summary>
    public LoggingSettings Logging { get; set; } = new();

    /// <summary>
    /// Gets or sets APRS-IS related settings.
    /// </summary>
    public AprsSettings AprsIs { get; set; } = new();

    [Obsolete("Use Logging.LogLevel instead.")]
    [JsonIgnore]
    public LogLevel LogLevel
    {
        get => Logging.LogLevel;
        set => Logging.LogLevel = value;
    }

    [Obsolete("Use Logging.WriteToFile instead.")]
    [JsonIgnore]
    public bool WriteToFile
    {
        get => Logging.WriteToFile;
        set => Logging.WriteToFile = value;
    }

    [Obsolete("Use AprsIs.Callsign instead.")]
    [JsonIgnore]
    public string Callsign
    {
        get => AprsIs.Callsign;
        set => AprsIs.Callsign = value;
    }

    [Obsolete("Use AprsIs.Passcode instead.")]
    [JsonIgnore]
    public string Passcode
    {
        get => AprsIs.Passcode;
        set => AprsIs.Passcode = value;
    }

    [Obsolete("Use AprsIs.Filter instead.")]
    [JsonIgnore]
    public string Filter
    {
        get => AprsIs.Filter;
        set => AprsIs.Filter = value;
    }

    /// <summary>
    /// Validates the settings, throwing if any value is invalid.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a value is outside its allowed range.</exception>
    public void Validate()
    {
        Logging.Validate();
    }

    /// <summary>
    /// Returns a copy of the current settings instance.
    /// </summary>
    public AppSettings Clone() => new()
    {
        Logging = Logging.Clone(),
        AprsIs = AprsIs.Clone(),
    };
}

public sealed class LoggingSettings
{
    /// <summary>
    /// Gets or sets the minimum log level emitted by the logging service.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Gets or sets a value indicating whether log messages should be written to a file.
    /// </summary>
    public bool WriteToFile { get; set; } = false;

    public void Validate()
    {
        if (!Enum.IsDefined(typeof(LogLevel), LogLevel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(LogLevel),
                LogLevel,
                $"Invalid log level. Must be one of: {string.Join(", ", Enum.GetNames(typeof(LogLevel)))}.");
        }
    }

    public LoggingSettings Clone() => new()
    {
        LogLevel = LogLevel,
        WriteToFile = WriteToFile,
    };
}

public sealed class AprsSettings
{
    /// <summary>
    /// Gets or sets the APRS-IS login callsign.
    /// </summary>
    public string Callsign { get; set; } = "N0CALL";

    /// <summary>
    /// Gets or sets the APRS-IS passcode.
    /// </summary>
    public string Passcode { get; set; } = "12345";

    /// <summary>
    /// Gets or sets the APRS-IS filter string.
    /// </summary>
    public string Filter { get; set; } = "m/50";

    public AprsSettings Clone() => new()
    {
        Callsign = Callsign,
        Passcode = Passcode,
        Filter = Filter,
    };
}

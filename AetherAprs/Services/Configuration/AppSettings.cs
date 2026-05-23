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
    /// Gets or sets the minimum log level emitted by the logging service.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Gets or sets a value indicating whether log messages should be written to a file.
    /// </summary>
    public bool WriteToFile { get; set; } = false;

    /// <summary>
    /// Gets or sets the APRS-IS login callsign.
    /// </summary>
    public string Callsign { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the APRS-IS passcode.
    /// </summary>
    public string Passcode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the APRS-IS filter string.
    /// </summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>
    /// Validates the settings, throwing if any value is invalid.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a value is outside its allowed range.</exception>
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

    /// <summary>
    /// Returns a copy of the current settings instance.
    /// </summary>
    public AppSettings Clone() => new()
    {
        LogLevel = LogLevel,
        WriteToFile = WriteToFile,
        Callsign = Callsign,
        Passcode = Passcode,
        Filter = Filter,
    };
}

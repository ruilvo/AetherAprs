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
    public required LoggingSettings Logging { get; set; }

    public required AprsSettings AprsIs { get; set; }

    public void Validate()
    {
        Logging.Validate();
    }

    public AppSettings Clone() => new()
    {
        Logging = Logging.Clone(),
        AprsIs = AprsIs.Clone(),
    };
}

public sealed class LoggingSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required LogLevel LogLevel { get; set; }

    public required bool WriteToFile { get; set; }

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
    public required string Host { get; set; }

    public required int Port { get; set; }

    public required string Callsign { get; set; }

    public required string Passcode { get; set; }

    public required string Filter { get; set; }

    public AprsSettings Clone() => new()
    {
        Host = Host,
        Port = Port,
        Callsign = Callsign,
        Passcode = Passcode,
        Filter = Filter,
    };
}

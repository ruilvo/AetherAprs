// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Logging;
using System.Text.Json.Serialization;

namespace AetherAprs.Services.Configuration;

public class AppSettings
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel LogLevel { get; set; } = LogLevel.Debug;

    public bool WriteToFile { get; set; } = false;
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text.Json.Serialization;

namespace AetherAprs.Configuration.Settings;

/// <summary>
/// Source-generated serialization metadata for the settings file.
/// </summary>
/// <remarks>
/// The settings document is written from a published snapshot, so its shape is
/// fully known at build time. Generating the metadata keeps the file format
/// working in a trimmed application, where reflection-based serialization is not
/// available. The APRS symbol enums carry their own converters and the remaining
/// enums are written as strings.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

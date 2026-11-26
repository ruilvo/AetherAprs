// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AetherAprs.Services.Transmission;

/// <summary>
/// Source-generated serialization metadata for the durable beacon transmit queue.
/// </summary>
/// <remarks>
/// The queue is a closed list of records, so its metadata is known at build time.
/// Generating it keeps the queue file readable in a trimmed application, where
/// reflection-based serialization is unavailable. The options match the web
/// defaults the file has always used, so existing files stay readable.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(List<BeaconQueueItem>))]
internal sealed partial class BeaconQueueJsonContext : JsonSerializerContext;

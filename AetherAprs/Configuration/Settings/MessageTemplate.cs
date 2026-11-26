// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

namespace AetherAprs.Configuration.Settings;

/// <summary>
/// Represents a user-defined quick message template for APRS messaging.
/// </summary>
public sealed record MessageTemplate
{
    /// <summary>
    /// Gets the display name of the template.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Gets the destination callsign for messages created from this template.
    /// </summary>
    public string Destination { get; init; } = "";

    /// <summary>
    /// Gets the message body. May contain placeholders such as
    /// {summit}, {freq}, {mode} and {comment}.
    /// </summary>
    public string Body { get; init; } = "";

    /// <summary>
    /// Gets optional initial values for named placeholders.
    /// </summary>
    public IReadOnlyDictionary<string, string> FieldDefaults { get; init; } =
        new Dictionary<string, string>();
}

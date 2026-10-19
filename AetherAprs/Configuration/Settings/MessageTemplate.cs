// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Configuration.Settings;

/// <summary>
/// Represents a user-defined quick message template for APRS messaging.
/// </summary>
public class MessageTemplate
{
    /// <summary>
    /// Gets or sets the display name of the template.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the destination callsign for messages created from this
    /// template.
    /// </summary>
    public string Destination { get; set; } = "";

    /// <summary>
    /// Gets or sets the message body. May contain placeholders such as
    /// {summit}, {freq}, {mode} and {comment}.
    /// </summary>
    public string Body { get; set; } = "";
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

namespace AetherAprs.Configuration.Settings;

/// <summary>
/// Settings for user-defined quick message templates.
/// </summary>
public class MessageTemplatesSettings
{
    /// <summary>
    /// Gets or sets the configured message templates.
    /// </summary>
    public List<MessageTemplate> Templates { get; set; } = new();
}

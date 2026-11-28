// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

namespace AetherAprs.Configuration.Settings;

/// <summary>
/// Settings for user-defined quick message templates.
/// </summary>
public sealed record MessageTemplatesSettings
{
    /// <summary>
    /// Gets the configured message templates, in display order.
    /// </summary>
    public IReadOnlyList<MessageTemplate> Templates { get; init; } = [];
}

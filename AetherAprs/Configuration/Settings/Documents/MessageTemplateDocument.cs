// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

namespace AetherAprs.Configuration.Settings.Documents;

/// <summary>
/// Mutable binding target for a message template. See
/// <see cref="AppSettingsDocument"/> for why this shape exists.
/// </summary>
internal sealed class MessageTemplateDocument
{
    public string Name { get; set; } = "";

    public string Destination { get; set; } = "";

    public string Body { get; set; } = "";

    public Dictionary<string, string> FieldDefaults { get; set; } = new();

    public MessageTemplate ToMessageTemplate() => new()
    {
        Name = Name,
        Destination = Destination,
        Body = Body,
        FieldDefaults = new Dictionary<string, string>(FieldDefaults),
    };
}

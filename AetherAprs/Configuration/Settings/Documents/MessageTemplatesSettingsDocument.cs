// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

namespace AetherAprs.Configuration.Settings.Documents;

/// <summary>
/// Mutable binding target for the <c>MessageTemplates</c> settings section. See
/// <see cref="AppSettingsDocument"/> for why this shape exists.
/// </summary>
internal sealed class MessageTemplatesSettingsDocument
{
    public List<MessageTemplateDocument> Templates { get; set; } = [];

    public MessageTemplatesSettings ToSettings()
    {
        var templates = new List<MessageTemplate>(Templates.Count);

        foreach (var template in Templates)
        {
            templates.Add(template.ToMessageTemplate());
        }

        return new MessageTemplatesSettings
        {
            Templates = templates,
        };
    }
}

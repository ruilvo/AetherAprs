// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration.Settings;
using AetherAprs.Models.Aprs;

namespace AetherAprs.Services.Messaging;

/// <summary>
/// Manages user-defined quick message templates: CRUD via the configuration
/// service, placeholder discovery/substitution, and filtering by peer callsign.
/// </summary>
public interface IMessageTemplateService
{
    /// <summary>
    /// Gets the maximum allowed length of a message produced from a template.
    /// APRS message text is limited to 67 characters.
    /// </summary>
    /// <remarks>
    /// Declared on the concrete <see cref="MessageTemplateService"/> as
    /// <see cref="MessageTemplateService.MaxTemplateMessageLength"/>.
    /// </remarks>
    IReadOnlyList<MessageTemplate> Templates { get; }

    /// <summary>
    /// Gets the template with the given name (case-insensitive), or null if not found.
    /// </summary>
    MessageTemplate? GetByName(string name);

    /// <summary>
    /// Appends a template to the configuration. The template name must not be
    /// null or blank.
    /// </summary>
    Task AddAsync(MessageTemplate messageTemplate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the first template whose name matches <paramref name="originalName"/>
    /// (case-insensitive). Does nothing if no template matches.
    /// </summary>
    Task UpdateAsync(string originalName, MessageTemplate messageTemplate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the template with the given name (case-insensitive).
    /// Does nothing if no template matches.
    /// </summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct placeholder names contained in the given body, in
    /// order of first appearance. Placeholders use the form {name} where name
    /// consists of letters, digits and underscores.
    /// </summary>
    IReadOnlyList<string> GetPlaceholderNames(string body);

    /// <summary>
    /// Substitutes the placeholders in the template body with the supplied
    /// values (trimmed, case-insensitive matching). Missing or blank values
    /// become empty strings. Returns null if the resulting message would
    /// exceed <see cref="MessageTemplateService.MaxTemplateMessageLength"/>
    /// characters.
    /// </summary>
    string? Apply(MessageTemplate messageTemplate, IReadOnlyDictionary<string, string> values);

    /// <summary>
    /// Returns the templates whose destination callsign base matches the given
    /// peer's base callsign (SSID is ignored).
    /// </summary>
    IReadOnlyList<MessageTemplate> GetTemplatesForPeer(Callsign peer);
}

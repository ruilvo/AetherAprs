// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration.Settings;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Configuration;

namespace AetherAprs.Services.Messaging;

/// <summary>
/// Default implementation of <see cref="IMessageTemplateService"/> backed by
/// <see cref="IConfigurationService"/>. Seeding and sanitization of the stored
/// templates are handled by the configuration service; this service does not
/// re-sanitize.
/// </summary>
public sealed partial class MessageTemplateService(IConfigurationService configurationService) : IMessageTemplateService
{
    /// <summary>
    /// Maximum allowed length of a message produced from a template.
    /// APRS message text is limited to 67 characters.
    /// </summary>
    public const int MaxTemplateMessageLength = 67;

    [GeneratedRegex(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex { get; }

    private readonly IConfigurationService _configurationService = configurationService;

    public IReadOnlyList<MessageTemplate> Templates =>
        [.. _configurationService.Current.MessageTemplates.Templates];

    public MessageTemplate? GetByName(string name) => _configurationService.Current.MessageTemplates.Templates
            .FirstOrDefault(template => string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase));

    public async Task AddAsync(MessageTemplate messageTemplate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageTemplate);
        if (string.IsNullOrWhiteSpace(messageTemplate.Name))
        {
            throw new ArgumentException("Template name must not be null or blank.", nameof(messageTemplate));
        }

        await _configurationService.UpdateAsync(settings => settings with
        {
            MessageTemplates = settings.MessageTemplates with
            {
                Templates = [.. settings.MessageTemplates.Templates, messageTemplate],
            },
        }, cancellationToken);
    }

    public async Task UpdateAsync(string originalName, MessageTemplate messageTemplate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageTemplate);

        await _configurationService.UpdateAsync(settings =>
        {
            MessageTemplate[] templates = [.. settings.MessageTemplates.Templates];
            int index = Array.FindIndex(
                templates,
                existing => string.Equals(existing.Name, originalName, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                return settings;
            }

            templates[index] = messageTemplate;

            return settings with
            {
                MessageTemplates = settings.MessageTemplates with
                {
                    Templates = templates,
                },
            };
        }, cancellationToken);
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default) => await _configurationService.UpdateAsync(settings => settings with
    {
        MessageTemplates = settings.MessageTemplates with
        {
            Templates =
                                                                                                              [
                                                                                                                  .. settings.MessageTemplates.Templates.Where(template =>
                        !string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase)),
                ],
        },
    }, cancellationToken);

    public IReadOnlyList<string> GetPlaceholderNames(string body)
    {
        List<string> names = [];
        if (string.IsNullOrEmpty(body))
        {
            return names;
        }

        foreach (Match match in PlaceholderRegex.Matches(body))
        {
            string name = match.Groups[1].Value;
            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    public string? Apply(MessageTemplate messageTemplate, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(messageTemplate);
        ArgumentNullException.ThrowIfNull(values);

        string result = messageTemplate.Body;
        foreach (string name in GetPlaceholderNames(result))
        {
            string? raw = null;
            foreach (KeyValuePair<string, string> pair in values)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    raw = pair.Value;
                    break;
                }
            }

            string value = !string.IsNullOrWhiteSpace(raw) ? raw.Trim() : string.Empty;
            result = result.Replace("{" + name + "}", value, StringComparison.OrdinalIgnoreCase);
        }

        return result.Length > MaxTemplateMessageLength ? null : result;
    }

    public IReadOnlyList<MessageTemplate> GetTemplatesForPeer(AprsAddress peer) =>
        // An addressee may be a callsign or a tactical address such as
        // "APRS2SOTA"; both are matched on the base so a template saved for
        // "SOTA" also applies to "SOTA-1".
        [.. _configurationService.Current.MessageTemplates.Templates
            .Where(template =>
                AprsAddress.TryParse(template.Destination, out AprsAddress destination)
                && destination.Base == peer.Base)];
}

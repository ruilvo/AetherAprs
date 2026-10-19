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
public sealed class MessageTemplateService : IMessageTemplateService
{
    /// <summary>
    /// Maximum allowed length of a message produced from a template.
    /// APRS message text is limited to 67 characters.
    /// </summary>
    public const int MaxTemplateMessageLength = 67;

    private static readonly Regex PlaceholderRegex = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    private readonly IConfigurationService _configurationService;

    public MessageTemplateService(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public IReadOnlyList<MessageTemplate> Templates =>
        _configurationService.Settings.MessageTemplates.Templates.ToArray();

    public MessageTemplate? GetByName(string name)
    {
        return _configurationService.Settings.MessageTemplates.Templates
            .FirstOrDefault(template => string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AddAsync(MessageTemplate messageTemplate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageTemplate);
        if (string.IsNullOrWhiteSpace(messageTemplate.Name))
        {
            throw new ArgumentException("Template name must not be null or blank.", nameof(messageTemplate));
        }

        await _configurationService.UpdateSettingsAsync(settings =>
            settings.MessageTemplates.Templates.Add(messageTemplate));
    }

    public async Task UpdateAsync(string originalName, MessageTemplate messageTemplate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageTemplate);

        await _configurationService.UpdateSettingsAsync(settings =>
        {
            var templates = settings.MessageTemplates.Templates;
            var index = templates.FindIndex(existing =>
                string.Equals(existing.Name, originalName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                templates[index] = messageTemplate;
            }
        });
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        await _configurationService.UpdateSettingsAsync(settings =>
            settings.MessageTemplates.Templates.RemoveAll(template =>
                string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    public IReadOnlyList<string> GetPlaceholderNames(string body)
    {
        var names = new List<string>();
        if (string.IsNullOrEmpty(body))
        {
            return names;
        }

        foreach (Match match in PlaceholderRegex.Matches(body))
        {
            var name = match.Groups[1].Value;
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

        var result = messageTemplate.Body;
        foreach (var name in GetPlaceholderNames(result))
        {
            string? raw = null;
            foreach (var pair in values)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    raw = pair.Value;
                    break;
                }
            }

            var value = !string.IsNullOrWhiteSpace(raw) ? raw.Trim() : string.Empty;
            result = result.Replace("{" + name + "}", value, StringComparison.OrdinalIgnoreCase);
        }

        return result.Length > MaxTemplateMessageLength ? null : result;
    }

    public IReadOnlyList<MessageTemplate> GetTemplatesForPeer(Callsign peer)
    {
        return _configurationService.Settings.MessageTemplates.Templates
            .Where(template =>
                Callsign.TryParse(template.Destination, out var destination)
                && destination.Base == peer.Base)
            .ToArray();
    }
}

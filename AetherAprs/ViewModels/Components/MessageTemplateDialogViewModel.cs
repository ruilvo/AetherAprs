// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using AetherAprs.Configuration.Settings;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherAprs.ViewModels.Components;

/// <summary>
/// View model for the message template picker dialog. Initialized with the
/// templates offered to the caller (optionally filtered by peer) via
/// <see cref="Initialize"/>; exposes per-placeholder fields, a live preview
/// of the applied message and a confirm command.
/// </summary>
public sealed partial class MessageTemplateDialogViewModel : ViewModelBase
{
    private static readonly Regex PlaceholderRegex = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    private bool _hasCallerDestination;

    /// <summary>
    /// Gets the templates the dialog was initialized with.
    /// </summary>
    public IReadOnlyList<MessageTemplate> Templates { get; private set; } = [];

    /// <summary>
    /// Gets the result of the dialog, or null until confirmed.
    /// </summary>
    public MessageTemplateDialogResult? Result { get; private set; }

    /// <summary>
    /// Raised when the user confirms, so the view can close the dialog.
    /// </summary>
    public event EventHandler? CloseRequested;

    [ObservableProperty]
    public partial MessageTemplate? SelectedTemplate { get; set; }

    [ObservableProperty]
    public partial string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Gets the fields for the selected template's placeholders.
    /// </summary>
    public ObservableCollection<TemplateFieldViewModel> Fields { get; } = new();

    /// <summary>
    /// Gets the template body with the current field values applied,
    /// or an empty string when no message can be built.
    /// </summary>
    public string Preview => ApplyCurrent() ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether a template is selected and the applied
    /// message fits within the APRS message length limit.
    /// </summary>
    public bool CanConfirm => SelectedTemplate != null && ApplyCurrent() != null;

    /// <summary>
    /// Initializes the dialog with the templates to pick from and an optional
    /// destination callsign. When a destination is supplied it is kept even
    /// when the selected template changes; otherwise selecting a template
    /// prefills the destination from the template.
    /// </summary>
    public void Initialize(IReadOnlyList<MessageTemplate> templates, string? destination)
    {
        Templates = templates ?? [];
        _hasCallerDestination = !string.IsNullOrEmpty(destination);
        Destination = destination ?? string.Empty;
        OnPropertyChanged(nameof(Templates));
    }

    partial void OnSelectedTemplateChanged(MessageTemplate? value)
    {
        RebuildFields(value);
    }

    private void RebuildFields(MessageTemplate? template)
    {
        foreach (var field in Fields)
        {
            field.PropertyChanged -= OnFieldPropertyChanged;
        }

        Fields.Clear();

        if (template != null)
        {
            foreach (var name in GetPlaceholderNames(template.Body))
            {
                var field = new TemplateFieldViewModel(name);
                field.PropertyChanged += OnFieldPropertyChanged;
                Fields.Add(field);
            }

            if (!_hasCallerDestination)
            {
                Destination = template.Destination ?? string.Empty;
            }
        }

        RaiseComputedPropertiesChanged();
    }

    private void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TemplateFieldViewModel.Value))
        {
            RaiseComputedPropertiesChanged();
        }
    }

    private void RaiseComputedPropertiesChanged()
    {
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(CanConfirm));
    }

    private string? ApplyCurrent()
    {
        if (SelectedTemplate == null)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in Fields)
        {
            values[field.Name] = field.Value;
        }

        return ApplyTemplate(SelectedTemplate, values);
    }

    [RelayCommand]
    private void Confirm()
    {
        var applied = ApplyCurrent();
        if (applied == null)
        {
            return;
        }

        Result = new MessageTemplateDialogResult(applied, Destination);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<string> GetPlaceholderNames(string body)
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

    private static string? ApplyTemplate(MessageTemplate template, IReadOnlyDictionary<string, string> values)
    {
        var result = template.Body;
        foreach (var name in GetPlaceholderNames(result))
        {
            values.TryGetValue(name, out var raw);
            var value = !string.IsNullOrWhiteSpace(raw) ? raw.Trim() : string.Empty;
            result = result.Replace("{" + name + "}", value, StringComparison.OrdinalIgnoreCase);
        }

        return result.Length > MessageTemplateService.MaxTemplateMessageLength ? null : result;
    }
}

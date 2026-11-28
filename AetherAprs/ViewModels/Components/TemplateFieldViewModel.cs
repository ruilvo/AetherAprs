// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.ViewModels.Components;

/// <summary>
/// Represents a single placeholder field of a message template,
/// e.g. "summit" for the <c>{summit}</c> placeholder.
/// </summary>
public sealed partial class TemplateFieldViewModel(string name) : ViewModelBase
{

    /// <summary>
    /// Gets the placeholder name, without braces.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets the localized display name for the placeholder.
    /// Returns a localized name for known placeholders, or the original name
    /// for unknown placeholders.
    /// </summary>
    public string DisplayName
    {
        get
        {
            string key = $"TemplateField_{Name}";
            string localized = Strings.Get(key);
            return localized != key ? localized : Name;
        }
    }

    /// <summary>
    /// Gets or sets the user-supplied value for the placeholder.
    /// </summary>
    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;
}

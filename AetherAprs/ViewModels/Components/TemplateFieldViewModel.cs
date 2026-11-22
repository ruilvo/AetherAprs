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
public sealed partial class TemplateFieldViewModel : ViewModelBase
{
    public TemplateFieldViewModel(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the placeholder name, without braces.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the localized display name for the placeholder.
    /// Returns a localized name for known placeholders, or the original name
    /// for unknown placeholders.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var key = $"TemplateField_{Name}";
            var localized = Strings.Get(key);
            return localized != key ? localized : Name;
        }
    }

    /// <summary>
    /// Gets or sets the user-supplied value for the placeholder.
    /// </summary>
    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;
}

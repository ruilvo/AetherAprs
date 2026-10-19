// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

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
    /// Gets or sets the user-supplied value for the placeholder.
    /// </summary>
    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;
}

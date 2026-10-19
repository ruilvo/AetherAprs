// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia.Metadata;
using Avalonia.Markup.Xaml;
using System;

namespace AetherAprs.Localization;

public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    /// <summary>
    /// Gets or sets the localization resource key.
    /// </summary>
    // TODO: values are resolved once at XAML load; a runtime language switch does
    // not refresh already-rendered views. Implement live re-evaluation if runtime
    // language switching is ever required.
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Strings.Get(Key);
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

using AetherAprs.Configuration.Settings;
using AetherAprs.ViewModels.Components;

namespace AetherAprs.Factories.ViewModels;

/// <summary>
/// Factory for creating and initializing MessageTemplateEditViewModel instances.
/// </summary>
public interface IMessageTemplateEditViewModelFactory
{
    /// <summary>
    /// Creates an edit view model initialized with the template to edit
    /// (null for add mode) and the existing template names for duplicate
    /// detection.
    /// </summary>
    MessageTemplateEditViewModel Create(MessageTemplate? original, IReadOnlyList<string> existingNames);
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration.Settings;
using AetherAprs.ViewModels.Components;
using System.Collections.Generic;

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

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using AetherAprs.Configuration.Settings;
using AetherAprs.ViewModels.Components;

namespace AetherAprs.Factories.ViewModels;

/// <summary>
/// Factory for creating and initializing MessageTemplateDialogViewModel instances.
/// </summary>
public interface IMessageTemplateDialogViewModelFactory
{
    /// <summary>
    /// Creates a MessageTemplateDialogViewModel initialized with the given
    /// templates and an optional destination callsign.
    /// </summary>
    /// <param name="templates">The templates to offer in the dialog.</param>
    /// <param name="destination">
    /// The destination callsign to lock the dialog to, or null to let the
    /// selected template prefill the destination.
    /// </param>
    /// <returns>An initialized MessageTemplateDialogViewModel.</returns>
    MessageTemplateDialogViewModel Create(IReadOnlyList<MessageTemplate> templates, string? destination);
}

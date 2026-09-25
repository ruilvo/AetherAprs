// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;

namespace AetherAprs.Factories;

/// <summary>
/// Factory for creating and initializing ConversationViewModel instances.
/// </summary>
public interface IConversationViewModelFactory
{
    /// <summary>
    /// Creates a ConversationViewModel initialized with the specified callsign.
    /// </summary>
    /// <param name="callsign">The callsign to start a conversation with.</param>
    /// <returns>An initialized ConversationViewModel.</returns>
    ViewModels.Pages.ConversationViewModel Create(Callsign callsign);
}

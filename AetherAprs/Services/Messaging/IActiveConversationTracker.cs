// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;

namespace AetherAprs.Services.Messaging;

/// <summary>
/// Navigation state for the conversation the operator is currently looking at.
/// It lives with the presentation layer, which owns navigation, while the messaging
/// service only reads it to suppress notifications for the visible conversation.
/// </summary>
public interface IActiveConversationTracker
{
    /// <summary>The conversation on screen, or <see langword="null"/> when none is.</summary>
    AprsAddress? Peer { get; }

    /// <summary>
    /// Marks <paramref name="peer"/> as the conversation on screen. A
    /// <see langword="null"/> peer clears the value.
    /// </summary>
    void Activate(AprsAddress? peer);

    /// <summary>
    /// Clears the conversation only if it still matches <paramref name="peer"/>, so a
    /// conversation being replaced does not wipe the state of the newly opened one.
    /// </summary>
    void Clear(AprsAddress peer);
}

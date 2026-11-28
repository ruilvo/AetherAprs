// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;

namespace AetherAprs.Services.Messaging;

public interface IMessageService
{
    /// <summary>
    /// Immutable snapshot of the conversations, newest first. ViewModels mirror it
    /// into their own observable collection and listen to
    /// <see cref="ConversationsChanged"/> for updates.
    /// </summary>
    IReadOnlyList<ConversationThread> Conversations { get; }

    /// <summary>
    /// Raised on the UI thread after the conversation set, its order or a
    /// conversation's messages changed.
    /// </summary>
    event EventHandler? ConversationsChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SendAsync(AprsAddress addressee, string text, CancellationToken cancellationToken = default);

    ConversationThread GetOrCreateConversation(AprsAddress peer);
}

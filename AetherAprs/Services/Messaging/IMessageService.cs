// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Messaging;

public interface IMessageService
{
    ObservableCollection<ConversationThread> Conversations { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SendAsync(AprsAddress addressee, string text, CancellationToken cancellationToken = default);

    ConversationThread GetOrCreateConversation(AprsAddress peer);

    /// <summary>
    /// Marks the given peer as the conversation currently being viewed, so that
    /// incoming messages from that peer do not raise a notification.
    /// A <see langword="null"/> peer clears the active conversation.
    /// </summary>
    void SetActiveConversation(AprsAddress? peer);

    /// <summary>
    /// Clears the active conversation only if it still matches <paramref name="peer"/>.
    /// Used on overlay retirement so a conversation being replaced does not wipe the
    /// active state of a newly opened conversation.
    /// </summary>
    void ClearActiveConversation(AprsAddress peer);
}

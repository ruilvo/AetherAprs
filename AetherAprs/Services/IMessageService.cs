// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;

namespace AetherAprs.Services;

public interface IMessageService
{
    ObservableCollection<ConversationThread> Conversations { get; }

    Task SendAsync(Callsign addressee, string text, CancellationToken cancellationToken = default);

    ConversationThread GetOrCreateConversation(Callsign peer);
}

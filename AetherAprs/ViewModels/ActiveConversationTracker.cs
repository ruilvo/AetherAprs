// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading;

using AetherAprs.Models.Aprs;
using AetherAprs.Services.Messaging;

namespace AetherAprs.ViewModels;

/// <summary>
/// Presentation-owned navigation state for the visible conversation. Thread-safe
/// because the messaging service reads it from the packet pipeline.
/// </summary>
public sealed class ActiveConversationTracker : IActiveConversationTracker
{
    private readonly Lock _gate = new();
    private AprsAddress? _peer;

    public AprsAddress? Peer
    {
        get
        {
            lock (_gate)
            {
                return _peer;
            }
        }
    }

    public void Activate(AprsAddress? peer)
    {
        lock (_gate)
        {
            _peer = peer;
        }
    }

    public void Clear(AprsAddress peer)
    {
        lock (_gate)
        {
            if (_peer is not null && _peer.Equals(peer))
            {
                _peer = null;
            }
        }
    }
}

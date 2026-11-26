// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace AetherAprs.Models.Messaging;

/// <summary>
/// A conversation thread with a peer address (callsign or tactical address).
/// The messages are an immutable snapshot; the owning view model mirrors them into
/// its own observable collection when <see cref="MessagesChanged"/> fires.
/// </summary>
public sealed class ConversationThread : INotifyPropertyChanged
{
    private readonly Lock _gate = new();
    private readonly List<StoredMessage> _messages = [];

    public ConversationThread(AprsAddress peer) => Peer = peer;

    public AprsAddress Peer { get; }

    public string PeerDisplay => Peer.ToString();

    /// <summary>Snapshot of the thread's messages, oldest first.</summary>
    public IReadOnlyList<StoredMessage> Messages
    {
        get
        {
            lock (_gate)
            {
                return [.. _messages];
            }
        }
    }

    public string Preview
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count == 0 ? string.Empty : _messages[^1].Text;
            }
        }
    }

    public DateTimeOffset? LastTimestamp
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count == 0 ? null : _messages[^1].Timestamp;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after a message is appended to the thread.</summary>
    public event EventHandler? MessagesChanged;

    /// <summary>Appends <paramref name="message"/> and notifies readers.</summary>
    public void Add(StoredMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_gate)
        {
            _messages.Add(message);
        }

        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(LastTimestamp));
        MessagesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

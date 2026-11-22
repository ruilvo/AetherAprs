// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AetherAprs.Models.Messaging;

/// <summary>
/// A conversation thread with a peer address (callsign or tactical address).
/// </summary>
public sealed class ConversationThread : INotifyPropertyChanged
{
    public ConversationThread(AprsAddress peer)
    {
        Peer = peer;
        Messages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Preview));
            OnPropertyChanged(nameof(LastTimestamp));
        };
    }

    public AprsAddress Peer { get; }

    public string PeerDisplay => Peer.ToString();

    public ObservableCollection<StoredMessage> Messages { get; } = [];

    public string Preview => Messages.LastOrDefault()?.Text ?? string.Empty;

    public DateTimeOffset? LastTimestamp => Messages.LastOrDefault()?.Timestamp;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

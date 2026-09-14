// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;

namespace AetherAprs.Services;

public sealed class MessageService : IMessageService, IDisposable
{
    private readonly IPortService _portService;
    private readonly IConfigurationService _configurationService;
    private readonly IAprsPortSettingsResolver _portSettingsResolver;
    private readonly ILogger<MessageService> _logger;
    private readonly object _gate = new();
    private int _nextMessageNumber = 1;
    private bool _disposed;

    public ObservableCollection<ConversationThread> Conversations { get; } = new();

    public MessageService(
        IPortService portService,
        IConfigurationService configurationService,
        IAprsPortSettingsResolver portSettingsResolver,
        ILogger<MessageService> logger)
    {
        _portService = portService;
        _configurationService = configurationService;
        _portSettingsResolver = portSettingsResolver;
        _logger = logger;
        _portService.PacketReceived += OnPacketReceived;
    }

    public ConversationThread GetOrCreateConversation(Callsign peer)
    {
        lock (_gate)
        {
            return GetOrCreateConversationUnlocked(peer);
        }
    }

    public async Task SendAsync(Callsign addressee, string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        text = text.Trim();
        if (text.Length > 67)
        {
            throw new ArgumentException("APRS message text must be 67 characters or fewer.", nameof(text));
        }

        var txPorts = _portService.Ports.Where(p => p.IsEnabled && p.IsTx).ToList();
        if (txPorts.Count == 0)
        {
            throw new InvalidOperationException("No enabled TX ports are available to send the message.");
        }

        var baseCallsign = _configurationService.Settings.Aprs.Callsign;
        var messageNumber = Interlocked.Increment(ref _nextMessageNumber);
        var timestamp = DateTimeOffset.UtcNow;
        Exception? lastError = null;
        var sent = 0;

        foreach (var port in txPorts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var sourceCallsign = ParsePortCallsign(_portSettingsResolver.GetPortCallsign(port, baseCallsign));
                var packet = new MessagePacket
                {
                    Source = sourceCallsign,
                    Destination = new Callsign("APRS"),
                    Addressee = addressee,
                    Text = text,
                    MessageNumber = messageNumber,
                    Timestamp = timestamp
                };

                await _portService.SendPacketAsync(port.Id, packet).ConfigureAwait(false);
                sent++;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogError(ex, "Failed to send APRS message on port {PortName}", port.Name);
            }
        }

        if (sent == 0)
        {
            throw lastError ?? new InvalidOperationException("Failed to send APRS message.");
        }

        RunOnUi(() =>
        {
            lock (_gate)
            {
                var thread = GetOrCreateConversationUnlocked(addressee);
                thread.Messages.Add(new StoredMessage
                {
                    Peer = addressee,
                    Text = text,
                    Timestamp = timestamp,
                    IsOutbound = true,
                    MessageNumber = messageNumber
                });
                MoveConversationToFront(thread);
            }
        });
    }

    private void OnPacketReceived(object? sender, PortPacketReceivedEventArgs e)
    {
        if (e.Packet is not MessagePacket message)
        {
            return;
        }

        var ownBase = _configurationService.Settings.Aprs.Callsign.Trim().ToUpperInvariant();
        var addressedToUs = string.Equals(message.Addressee.Base, ownBase, StringComparison.Ordinal);
        var fromUs = string.Equals(message.Source.Base, ownBase, StringComparison.Ordinal);

        // Only store inbound messages addressed to us. Outbound is recorded in SendAsync.
        if (!addressedToUs || fromUs)
        {
            return;
        }

        var peer = message.Source;
        var stored = new StoredMessage
        {
            Peer = peer,
            Text = message.Text,
            Timestamp = message.Timestamp ?? DateTimeOffset.UtcNow,
            IsOutbound = false,
            MessageNumber = message.MessageNumber,
            PortId = e.PortId
        };

        RunOnUi(() =>
        {
            lock (_gate)
            {
                var thread = GetOrCreateConversationUnlocked(peer);
                thread.Messages.Add(stored);
                MoveConversationToFront(thread);
            }
        });
    }

    private ConversationThread GetOrCreateConversationUnlocked(Callsign peer)
    {
        var existing = Conversations.FirstOrDefault(c => c.Peer.Equals(peer));
        if (existing != null)
        {
            return existing;
        }

        var created = new ConversationThread(peer);
        Conversations.Insert(0, created);
        return created;
    }

    private void MoveConversationToFront(ConversationThread thread)
    {
        var index = Conversations.IndexOf(thread);
        if (index > 0)
        {
            Conversations.Move(index, 0);
        }
    }

    private static Callsign ParsePortCallsign(string callsign)
    {
        if (Callsign.TryParse(callsign, out var parsed))
        {
            return parsed;
        }

        var parts = callsign.Split('-');
        var callsignBase = parts[0];
        int? ssid = parts.Length > 1 && int.TryParse(parts[1], out var ssidValue) ? ssidValue : null;
        return new Callsign(callsignBase, ssid);
    }

    private static void RunOnUi(Action action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _portService.PacketReceived -= OnPacketReceived;
    }
}



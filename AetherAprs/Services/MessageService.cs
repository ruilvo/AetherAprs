// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using Avalonia;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services;

public sealed class MessageService : IMessageService, IDisposable
{
    private readonly IPortService _portService;
    private readonly IPacketStorageService _packetStorageService;
    private readonly IConfigurationService _configurationService;
    private readonly IAprsPortSettingsResolver _portSettingsResolver;
    private readonly ILogger<MessageService> _logger;
    private readonly IDbContextFactory<AppDbContext>? _dbContextFactory;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, StoredMessage> _pendingMessages = new();
    private readonly Timer _retryTimer;
    private int _nextMessageNumber = 1;
    private bool _disposed;

    public ObservableCollection<ConversationThread> Conversations { get; } = new();

    public MessageService(
        IPortService portService,
        IPacketStorageService packetStorageService,
        IConfigurationService configurationService,
        IAprsPortSettingsResolver portSettingsResolver,
        ILogger<MessageService> logger,
        IDbContextFactory<AppDbContext>? dbContextFactory = null)
    {
        _portService = portService;
        _packetStorageService = packetStorageService;
        _configurationService = configurationService;
        _portSettingsResolver = portSettingsResolver;
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _portService.PacketReceived += OnPacketReceived;
        _retryTimer = new Timer(ProcessRetries, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        LoadMessages();
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

        var messageNumber = Interlocked.Increment(ref _nextMessageNumber);
        var timestamp = DateTimeOffset.UtcNow;
        var settings = _configurationService.Settings.Aprs;

        var stored = new StoredMessage
        {
            Peer = addressee,
            Text = text,
            Timestamp = timestamp,
            IsOutbound = true,
            MessageNumber = messageNumber,
            DeliveryStatus = MessageDeliveryStatus.Pending,
            RetryCount = 0,
            NextRetryTime = timestamp.AddSeconds(settings.MessageRetryTimeoutSeconds)
        };

        lock (_gate)
        {
            _pendingMessages[messageNumber] = stored;
        }

        await SendMessagePacketAsync(stored, cancellationToken).ConfigureAwait(false);

        RunOnUi(() =>
        {
            lock (_gate)
            {
                var thread = GetOrCreateConversationUnlocked(addressee);
                thread.Messages.Add(stored);
                MoveConversationToFront(thread);
            }
        });
    }

    private async Task SendMessagePacketAsync(StoredMessage message, CancellationToken cancellationToken = default)
    {
        var txPorts = _portService.Ports.Where(p => p.IsEnabled && p.IsTx).ToList();
        if (txPorts.Count == 0)
        {
            throw new InvalidOperationException("No enabled TX ports available to send message.");
        }

        var baseCallsign = _configurationService.Settings.Aprs.Callsign;
        Exception? lastError = null;
        var sent = 0;

        foreach (var port in txPorts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var sourceCallsign = ParsePortCallsign(_portSettingsResolver.GetCallsign(baseCallsign));
                var packet = new MessagePacket
                {
                    Source = sourceCallsign,
                    Destination = new Callsign("APRS"),
                    Addressee = message.Peer,
                    Text = message.Text,
                    MessageNumber = message.MessageNumber,
                    Timestamp = message.Timestamp
                };

                await _portService.SendPacketAsync(port.Id, packet).ConfigureAwait(false);
                
                // Store sent packet in database
                await _packetStorageService.StorePacketAsync(packet, port.Id, isOutbound: true, cancellationToken).ConfigureAwait(false);
                
                sent++;
                _logger.LogInformation(
                    "Sent APRS message to {Addressee} on port {PortName} (msg#{MessageNumber}, retry {RetryCount})",
                    message.Peer,
                    port.Name,
                    message.MessageNumber,
                    message.RetryCount);
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogError(ex, "Failed to send APRS message on port {PortName}", port.Name);
            }
        }

        if (sent == 0 && lastError != null)
        {
            _logger.LogError(lastError, "Failed to send APRS message to {Addressee}", message.Peer);
        }
    }

    private async Task SendAckAsync(Callsign addressee, int messageNumber, Guid? portId)
    {
        var baseCallsign = _configurationService.Settings.Aprs.Callsign;
        var sourceCallsign = ParsePortCallsign(_portSettingsResolver.GetCallsign(baseCallsign));

        var ackText = $"ack{messageNumber}";
        var packet = new MessagePacket
        {
            Source = sourceCallsign,
            Destination = new Callsign("APRS"),
            Addressee = addressee,
            Text = ackText,
            Timestamp = DateTimeOffset.UtcNow
        };

        try
        {
            if (portId.HasValue)
            {
                await _portService.SendPacketAsync(portId.Value, packet).ConfigureAwait(false);
                await _packetStorageService.StorePacketAsync(packet, portId.Value, isOutbound: true).ConfigureAwait(false);
                _logger.LogInformation("Sent ACK to {Addressee} for message #{MessageNumber}", addressee, messageNumber);
            }
            else
            {
                var txPorts = _portService.Ports.Where(p => p.IsEnabled && p.IsTx).ToList();
                foreach (var port in txPorts)
                {
                    await _portService.SendPacketAsync(port.Id, packet).ConfigureAwait(false);
                    await _packetStorageService.StorePacketAsync(packet, port.Id, isOutbound: true).ConfigureAwait(false);
                }
                _logger.LogInformation("Sent ACK to {Addressee} for message #{MessageNumber} on all TX ports", addressee, messageNumber);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send ACK to {Addressee} for message #{MessageNumber}", addressee, messageNumber);
        }
    }

    private void OnPacketReceived(object? sender, PortPacketReceivedEventArgs e)
    {
        var ownBase = _configurationService.Settings.Aprs.Callsign.Trim().ToUpperInvariant();

        if (e.Packet is MessagePacket message)
        {
            var addressedToUs = string.Equals(message.Addressee.Base, ownBase, StringComparison.Ordinal);
            var fromUs = string.Equals(message.Source.Base, ownBase, StringComparison.Ordinal);

            if (addressedToUs && !fromUs)
            {
                HandleIncomingMessage(message, e.PortId);
            }
        }
        else if (e.Packet is MessageAckPacket ack)
        {
            var addressedToUs = string.Equals(ack.Addressee.Base, ownBase, StringComparison.Ordinal);
            if (addressedToUs)
            {
                HandleAck(ack);
            }
        }
        else if (e.Packet is MessageRejPacket rej)
        {
            var addressedToUs = string.Equals(rej.Addressee.Base, ownBase, StringComparison.Ordinal);
            if (addressedToUs)
            {
                HandleRej(rej);
            }
        }
    }

    private void HandleIncomingMessage(MessagePacket message, Guid portId)
    {
        var peer = message.Source;
        var stored = new StoredMessage
        {
            Peer = peer,
            Text = message.Text,
            Timestamp = message.Timestamp ?? DateTimeOffset.UtcNow,
            IsOutbound = false,
            MessageNumber = message.MessageNumber,
            PortId = portId
        };

        _logger.LogInformation(
            "Received APRS message from {Peer} on port {PortId}: {Text}",
            peer,
            portId,
            message.Text);

        RunOnUi(() =>
        {
            lock (_gate)
            {
                var thread = GetOrCreateConversationUnlocked(peer);
                thread.Messages.Add(stored);
                MoveConversationToFront(thread);
            }
        });

        // Auto-send ACK if enabled and message has a number
        if (_configurationService.Settings.Aprs.AutoAcknowledgeMessages && message.MessageNumber.HasValue)
        {
            _ = SendAckAsync(peer, message.MessageNumber.Value, portId);
        }
    }

    private void HandleAck(MessageAckPacket ack)
    {
        _logger.LogInformation("Received ACK from {Source} for message #{MessageNumber}", ack.Source, ack.MessageNumber);

        lock (_gate)
        {
            if (_pendingMessages.TryGetValue(ack.MessageNumber, out var message))
            {
                message.DeliveryStatus = MessageDeliveryStatus.Acknowledged;
                _pendingMessages.Remove(ack.MessageNumber);
                UpdateMessageStatus(message);
            }
        }
    }

    private void HandleRej(MessageRejPacket rej)
    {
        _logger.LogInformation("Received REJ from {Source} for message #{MessageNumber}", rej.Source, rej.MessageNumber);

        lock (_gate)
        {
            if (_pendingMessages.TryGetValue(rej.MessageNumber, out var message))
            {
                message.DeliveryStatus = MessageDeliveryStatus.Rejected;
                _pendingMessages.Remove(rej.MessageNumber);
                UpdateMessageStatus(message);
            }
        }
    }

    private void ProcessRetries(object? state)
    {
        if (_disposed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var settings = _configurationService.Settings.Aprs;
        List<StoredMessage> toRetry = new();

        lock (_gate)
        {
            foreach (var kvp in _pendingMessages.ToList())
            {
                var message = kvp.Value;
                if (message.NextRetryTime.HasValue && message.NextRetryTime.Value <= now)
                {
                    if (message.RetryCount >= settings.MessageMaxRetries)
                    {
                        // Timeout
                        message.DeliveryStatus = MessageDeliveryStatus.Timeout;
                        _pendingMessages.Remove(kvp.Key);
                        UpdateMessageStatus(message);
                        _logger.LogWarning("Message #{MessageNumber} to {Peer} timed out after {RetryCount} retries",
                            message.MessageNumber, message.Peer, message.RetryCount);
                    }
                    else
                    {
                        // Schedule retry
                        message.RetryCount++;
                        var backoff = settings.MessageRetryTimeoutSeconds * Math.Pow(2, message.RetryCount - 1);
                        message.NextRetryTime = now.AddSeconds(backoff);
                        toRetry.Add(message);
                        UpdateMessageStatus(message);
                    }
                }
            }
        }

        // Retry outside the lock
        foreach (var message in toRetry)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendMessagePacketAsync(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrying message #{MessageNumber}", message.MessageNumber);
                }
            });
        }
    }

    private void UpdateMessageStatus(StoredMessage message)
    {
        if (_dbContextFactory is null)
        {
            return;
        }

        try
        {
            using var db = _dbContextFactory.CreateDbContext();
            
            // Convert SSID for database comparison
            var addresseeSsid = message.Peer.Ssid.HasValue 
                ? (AprsSsid)message.Peer.Ssid.Value 
                : AprsSsid.PrimaryStation;
            
            // Find the message packet record
            var record = db.Packets
                .Include(p => p.Message)
                .FirstOrDefault(p =>
                    p.PacketType == "Message" &&
                    p.Message != null &&
                    p.Message.AddresseeBase == message.Peer.Base &&
                    p.Message.AddresseeSsid == addresseeSsid &&
                    p.Timestamp == message.Timestamp.UtcDateTime &&
                    p.IsOutbound == message.IsOutbound);

            if (record?.Message != null)
            {
                record.Message.DeliveryStatus = message.DeliveryStatus;
                record.Message.RetryCount = message.RetryCount;
                record.Message.NextRetryTime = message.NextRetryTime?.UtcDateTime;
                db.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update message status for {Peer}", message.Peer);
        }
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

    private void LoadMessages()
    {
        if (_dbContextFactory is null)
        {
            return;
        }

        using var db = _dbContextFactory.CreateDbContext();
        
        // Load message packets from the Packets table
        var records = db.Packets
            .Include(p => p.Message)
            .Where(p => p.PacketType == "Message" && p.Message != null)
            .AsNoTracking()
            .AsEnumerable()
            .OrderBy(p => p.Timestamp)
            .ThenBy(p => p.Id)
            .ToList();

        var threadsByPeer = new Dictionary<string, ConversationThread>();
        var maxMessageNumber = 1;
        
        foreach (var record in records)
        {
            if (record.Message == null)
            {
                continue;
            }

            var source = new Callsign(record.SourceBase, (int)record.SourceSsid);
            var addressee = new Callsign(record.Message.AddresseeBase, (int)record.Message.AddresseeSsid);
            
            // Determine peer (who we're talking to)
            var ownBase = _configurationService.Settings.Aprs.Callsign.Trim().ToUpperInvariant();
            var peer = record.IsOutbound ? addressee : source;

            var stored = new StoredMessage
            {
                Peer = peer,
                Text = record.Message.Text,
                Timestamp = new DateTimeOffset(record.Timestamp, TimeSpan.Zero),
                IsOutbound = record.IsOutbound,
                MessageNumber = record.Message.Number,
                PortId = record.PortId,
                DeliveryStatus = record.Message.DeliveryStatus,
                RetryCount = record.Message.RetryCount,
                NextRetryTime = record.Message.NextRetryTime.HasValue 
                    ? new DateTimeOffset(record.Message.NextRetryTime.Value, TimeSpan.Zero) 
                    : null
            };

            if (!threadsByPeer.TryGetValue(peer.ToString(), out var thread))
            {
                thread = new ConversationThread(peer);
                threadsByPeer[peer.ToString()] = thread;
            }

            thread.Messages.Add(stored);
            
            // Re-add pending messages to retry queue
            if (stored.IsOutbound && 
                stored.MessageNumber.HasValue && 
                stored.DeliveryStatus == MessageDeliveryStatus.Pending)
            {
                _pendingMessages[stored.MessageNumber.Value] = stored;
            }

            if (stored.MessageNumber is { } number && number > maxMessageNumber)
            {
                maxMessageNumber = number;
            }
        }

        foreach (var thread in threadsByPeer.Values.OrderByDescending(thread => thread.LastTimestamp))
        {
            Conversations.Add(thread);
        }

        _nextMessageNumber = maxMessageNumber + 1;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _retryTimer.Dispose();
        _portService.PacketReceived -= OnPacketReceived;
    }
}

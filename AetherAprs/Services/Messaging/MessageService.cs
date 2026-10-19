// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Factories.Packets;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Ports;
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

namespace AetherAprs.Services.Messaging;

public sealed class MessageService : IMessageService, IDisposable
{
    /// <summary>Pocket-packet discriminator stored in the database (PacketType column).</summary>
    private const string MessagePacketType = "Message";

    /// <summary>APRS message text limit (67 characters) per the APRS specification.</summary>
    private const int AprsMessageTextMaxLength = 67;

    /// <summary>Maximum number of message packets loaded from the database at startup.</summary>
    private const int MaxLoadedMessages = 500;

    /// <summary>Maximum number of recently received messages tracked for duplicate suppression.</summary>
    private const int DedupTableCapacity = 256;

    /// <summary>How long a message stays in the dedup table before it may be received again.</summary>
    private static readonly TimeSpan DedupRetention = TimeSpan.FromSeconds(30);

    private readonly IPortService _portService;
    private readonly IPacketStorageService _packetStorageService;
    private readonly IConfigurationService _configurationService;
    private readonly IAprsPortSettingsResolver _portSettingsResolver;
    private readonly IPacketFactory _packetFactory;
    private readonly ILogger<MessageService> _logger;
    private readonly IDbContextFactory<AppDbContext>? _dbContextFactory;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, StoredMessage> _pendingMessages = new();
    private readonly object _dedupLock = new();
    private readonly Queue<(string Key, DateTimeOffset Seen)> _dedupOrder = new();
    private readonly Dictionary<string, DateTimeOffset> _dedupTable = new();
    private readonly Timer _retryTimer;
    private int _nextMessageNumber = 1;
    private bool _disposed;

    public ObservableCollection<ConversationThread> Conversations { get; } = new();

    public MessageService(
        IPortService portService,
        IPacketStorageService packetStorageService,
        IConfigurationService configurationService,
        IAprsPortSettingsResolver portSettingsResolver,
        IPacketFactory packetFactory,
        ILogger<MessageService> logger,
        IDbContextFactory<AppDbContext>? dbContextFactory = null)
    {
        _portService = portService;
        _packetStorageService = packetStorageService;
        _configurationService = configurationService;
        _portSettingsResolver = portSettingsResolver;
        _packetFactory = packetFactory;
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _portService.PacketReceived += OnPacketReceived;
        _retryTimer = new Timer(ProcessRetries, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadMessagesAsync(cancellationToken).ConfigureAwait(false);
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
        if (text.Length > AprsMessageTextMaxLength)
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

        try
        {
            await SendMessagePacketAsync(stored, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The initial send failed entirely: mark the message as failed so the
            // retry loop does not keep re-sending something the caller saw fail (H4).
            lock (_gate)
            {
                _pendingMessages.Remove(messageNumber);
                stored.DeliveryStatus = MessageDeliveryStatus.Failed;
            }
            PersistMessageStatusInBackground(stored);
            throw;
        }

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
                var callsignString = _portSettingsResolver.GetCallsign(baseCallsign);
                
                var packet = _packetFactory.CreateMessagePacket(
                    callsignString,
                    message.Peer,
                    message.Text,
                    message.MessageNumber);

                // Note: Cannot set Timestamp after construction (init-only property)
                // Factory creates packet with default timestamp, but we'll log the message timestamp

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
        var callsignString = _portSettingsResolver.GetCallsign(baseCallsign);

        var packet = _packetFactory.CreateAckPacket(callsignString, addressee, messageNumber);

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
        // Deduplicate messages: RF↔APRS-IS gating means the same message may arrive
        // on multiple ports within seconds. Track recently seen (source, number) pairs.
        if (message.MessageNumber.HasValue && IsDuplicateMessage(message.Source, message.MessageNumber.Value))
        {
            _logger.LogDebug(
                "Suppressing duplicate message #{MessageNumber} from {Source} on port {PortId}",
                message.MessageNumber.Value,
                message.Source,
                portId);
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

        StoredMessage? message;
        lock (_gate)
        {
            if (!_pendingMessages.TryGetValue(ack.MessageNumber, out message))
            {
                return;
            }

            // Only the message's addressee may acknowledge it; small integer
            // message numbers collide easily between stations (H5).
            if (!ack.Source.Equals(message.Peer))
            {
                _logger.LogWarning(
                    "Ignoring ACK from {Source} for message #{MessageNumber} addressed to {Peer}",
                    ack.Source,
                    ack.MessageNumber,
                    message.Peer);
                return;
            }

            message.DeliveryStatus = MessageDeliveryStatus.Acknowledged;
            _pendingMessages.Remove(ack.MessageNumber);
        }

        // Persist outside the lock: the DB call may block on SQLite I/O.
        PersistMessageStatusInBackground(message);
    }

    private void HandleRej(MessageRejPacket rej)
    {
        _logger.LogInformation("Received REJ from {Source} for message #{MessageNumber}", rej.Source, rej.MessageNumber);

        StoredMessage? message;
        lock (_gate)
        {
            if (!_pendingMessages.TryGetValue(rej.MessageNumber, out message))
            {
                return;
            }

            if (!rej.Source.Equals(message.Peer))
            {
                _logger.LogWarning(
                    "Ignoring REJ from {Source} for message #{MessageNumber} addressed to {Peer}",
                    rej.Source,
                    rej.MessageNumber,
                    message.Peer);
                return;
            }

            message.DeliveryStatus = MessageDeliveryStatus.Rejected;
            _pendingMessages.Remove(rej.MessageNumber);
        }

        PersistMessageStatusInBackground(message);
    }

    internal void ProcessRetries(object? state) // internal for tests (InternalsVisibleTo AetherAprs.Tests)
    {
        if (_disposed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var settings = _configurationService.Settings.Aprs;
        List<StoredMessage> toRetry = [];
        List<StoredMessage> timedOut = [];

        lock (_gate)
        {
            foreach (var kvp in _pendingMessages.ToList())
            {
                if (_disposed)
                {
                    return;
                }

                var message = kvp.Value;
                if (message.NextRetryTime.HasValue && message.NextRetryTime.Value <= now)
                {
                    if (message.RetryCount >= settings.MessageMaxRetries)
                    {
                        // Timeout
                        message.DeliveryStatus = MessageDeliveryStatus.Timeout;
                        _pendingMessages.Remove(kvp.Key);
                        timedOut.Add(message);
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
                    }
                }
            }
        }

        // Persist and retry outside the lock.
        foreach (var message in timedOut)
        {
            PersistMessageStatusInBackground(message);
        }

        foreach (var message in toRetry)
        {
            PersistMessageStatusInBackground(message);
            _ = Task.Run(async () =>
            {
                if (_disposed)
                {
                    return;
                }

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

    /// <summary>
    /// Persists a message status change on the background thread pool. Snapshot of
    /// the message state is taken from the (already mutable) <see cref="StoredMessage"/>,
    /// which is safe because status transitions only move forward once removed from
    /// <see cref="_pendingMessages"/>.
    /// </summary>
    private void PersistMessageStatusInBackground(StoredMessage message)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await UpdateMessageStatusAsync(message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist message status for {Peer}", message.Peer);
            }
        });
    }

    private async Task UpdateMessageStatusAsync(StoredMessage message)
    {
        if (_dbContextFactory is null)
        {
            return;
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);

        // Convert SSID for database comparison
        var addresseeSsid = message.Peer.Ssid.HasValue
            ? (AprsSsid)message.Peer.Ssid.Value
            : AprsSsid.PrimaryStation;

        // Find the message packet record. Match on the stable message identity
        // (number + addressee + direction) rather than exact timestamp equality,
        // which is fragile across SQLite TEXT datetime round-trips (M8).
        var record = await db.Packets
            .Include(p => p.Message)
            .Where(p =>
                p.PacketType == MessagePacketType &&
                p.Message != null &&
                p.Message.Number == message.MessageNumber &&
                p.Message.AddresseeBase == message.Peer.Base &&
                p.Message.AddresseeSsid == addresseeSsid &&
                p.IsOutbound == message.IsOutbound)
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync().ConfigureAwait(false);

        if (record?.Message != null)
        {
            record.Message.DeliveryStatus = message.DeliveryStatus;
            record.Message.RetryCount = message.RetryCount;
            record.Message.NextRetryTime = message.NextRetryTime?.UtcDateTime;
            await db.SaveChangesAsync().ConfigureAwait(false);
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

    private static void RunOnUi(Action action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    /// <summary>
    /// Records the message in the dedup table and returns whether it was already seen
    /// within the retention window.
    /// </summary>
    private bool IsDuplicateMessage(Callsign source, int messageNumber)
    {
        var key = $"{source}|{messageNumber}";
        var now = DateTimeOffset.UtcNow;

        lock (_dedupLock)
        {
            if (_dedupTable.TryGetValue(key, out var seen) && now - seen < DedupRetention)
            {
                // Refresh timestamp so a frequently repeated message does not cycle back in.
                _dedupTable[key] = now;
                return true;
            }

            _dedupTable[key] = now;
            _dedupOrder.Enqueue((key, now));

            // Bounded LRU: evict expired entries first, then by capacity.
            while (_dedupOrder.Count > 0
                && (now - _dedupOrder.Peek().Seen >= DedupRetention || _dedupOrder.Count > DedupTableCapacity))
            {
                var oldest = _dedupOrder.Dequeue();
                _dedupTable.Remove(oldest.Key);
            }

            return false;
        }
    }

    private async Task LoadMessagesAsync(CancellationToken cancellationToken = default)
    {
        if (_dbContextFactory is null)
        {
            return;
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Load message packets from the Packets table, bounded so that a large
        // packet database cannot stall startup (M11). Newer packets are preferred.
        var records = await db.Packets
            .Include(p => p.Message)
            .Where(p => p.PacketType == MessagePacketType && p.Message != null)
            .OrderByDescending(p => p.Id)
            .Take(MaxLoadedMessages)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Sort client-side: first by timestamp, then by ID
        records = [.. records.OrderBy(p => p.Timestamp).ThenBy(p => p.Id)];

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

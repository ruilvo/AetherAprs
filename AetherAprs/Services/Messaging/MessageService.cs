// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Data.Entities;
using AetherAprs.Factories.Packets;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Contracts;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Services.UI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Messaging;

public sealed class MessageService : IMessageService, IDisposable
{
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
    private readonly IPacketQueryService? _packetQueryService;
    private readonly INotificationService? _notificationService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly IActiveConversationTracker? _activeConversationTracker;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, StoredMessage> _pendingMessages = [];
    private readonly Lock _dedupLock = new();
    private readonly Queue<(string Key, DateTimeOffset Seen)> _dedupOrder = new();
    private readonly Dictionary<string, DateTimeOffset> _dedupTable = [];
    private readonly ITimer _retryTimer;
    private readonly Lock _persistLock = new();
    private TaskCompletionSource _persistSignal = CreatePersistSignal();
    private int _pendingPersists;
    private int _nextMessageNumber = 1;
    private bool _disposed;

    private readonly List<ConversationThread> _conversationList = [];

    /// <inheritdoc />
    public IReadOnlyList<ConversationThread> Conversations
    {
        get
        {
            lock (_gate)
            {
                return [.. _conversationList];
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? ConversationsChanged;

    /// <summary>
    /// How many outbound messages are still waiting for an ACK or a retry; exposed
    /// for tests that assert terminal work leaves the retry queue.
    /// </summary>
    internal int PendingOutboundCount
    {
        get
        {
            lock (_gate)
            {
                return _pendingMessages.Count;
            }
        }
    }

    /// <summary>
    /// Completes once every status change queued for background persistence before
    /// this call has been written. Lets tests assert the durable state without
    /// polling the database.
    /// </summary>
    internal async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task idle;
            lock (_persistLock)
            {
                idle = _persistSignal.Task;
            }

            if (Volatile.Read(ref _pendingPersists) == 0)
            {
                return;
            }

            await idle.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public MessageService(
        IPortService portService,
        IPacketStorageService packetStorageService,
        IConfigurationService configurationService,
        IAprsPortSettingsResolver portSettingsResolver,
        IPacketFactory packetFactory,
        ILogger<MessageService> logger,
        IUiDispatcher uiDispatcher,
        IPacketQueryService? packetQueryService = null,
        INotificationService? notificationService = null,
        TimeProvider? timeProvider = null,
        IActiveConversationTracker? activeConversationTracker = null)
    {
        _portService = portService;
        _packetStorageService = packetStorageService;
        _configurationService = configurationService;
        _portSettingsResolver = portSettingsResolver;
        _packetFactory = packetFactory;
        _logger = logger;
        _uiDispatcher = uiDispatcher;
        _packetQueryService = packetQueryService;
        _notificationService = notificationService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _activeConversationTracker = activeConversationTracker;
        _portService.PacketReceived += OnPacketReceived;
        TimeSpan retryInterval = TimeSpan.FromSeconds(5);
        _retryTimer = _timeProvider.CreateTimer(ProcessRetries, null, retryInterval, retryInterval);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default) => await LoadMessagesAsync(cancellationToken).ConfigureAwait(false);

    public ConversationThread GetOrCreateConversation(AprsAddress peer)
    {
        ConversationThread thread;
        lock (_gate)
        {
            thread = GetOrCreateConversationUnlocked(peer);
        }

        NotifyConversationsChanged();
        return thread;
    }

    public async Task SendAsync(AprsAddress addressee, string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        text = text.Trim();
        if (text.Length > AprsMessageTextMaxLength)
        {
            throw new ArgumentException("APRS message text must be 67 characters or fewer.", nameof(text));
        }

        int messageNumber = Interlocked.Increment(ref _nextMessageNumber);
        DateTimeOffset timestamp = _timeProvider.GetUtcNow();
        AprsSettings settings = _configurationService.Current.Aprs;

        StoredMessage stored = new()
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
            // Keep the item queued in memory. It becomes recoverable across
            // restarts once the outbox write succeeds.
            stored.NextRetryTime = _timeProvider.GetUtcNow().AddSeconds(
                settings.MessageRetryTimeoutSeconds);
            throw;
        }

        RunOnUi(() =>
        {
            lock (_gate)
            {
                ConversationThread thread = GetOrCreateConversationUnlocked(addressee);
                thread.Add(stored);
                MoveConversationToFront(thread);
            }
        });

        NotifyConversationsChanged();
    }

    private async Task SendMessagePacketAsync(StoredMessage message, CancellationToken cancellationToken = default)
    {
        List<PortConfig> txPorts = [.. _portService.Ports.Where(p => p.IsEnabled && p.IsTx)];
        if (txPorts.Count == 0)
        {
            throw new InvalidOperationException("No enabled TX ports available to send message.");
        }

        string baseCallsign = _configurationService.Current.Aprs.Callsign;
        Exception? lastError = null;
        int sent = 0;
        bool persisted = false;
        int retryCount;
        DateTimeOffset? nextRetryTime;
        lock (_gate)
        {
            retryCount = message.RetryCount;
            nextRetryTime = message.NextRetryTime;
        }

        foreach (PortConfig port in txPorts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string callsignString = _portSettingsResolver.GetCallsign(baseCallsign);

                MessagePacket packet = _packetFactory.CreateMessagePacket(
                    callsignString,
                    message.Peer,
                    message.Text,
                    message.MessageNumber);

                // Note: Cannot set Timestamp after construction (init-only property)
                // Factory creates packet with default timestamp, but we'll log the message timestamp

                // One row per logical message: persist on the first attempt, then
                // only update it from the retry/status path. Storing per attempt
                // produced up to MaximumRetries x TX-port immortal Pending rows
                //. The record is still written before the transport
                // call so a crash mid-send can be recovered at startup.
                if (!persisted)
                {
                    await _packetStorageService
                        .EnsureOutboundMessageAsync(packet, port.Id, retryCount, nextRetryTime, cancellationToken)
                        .ConfigureAwait(false);
                    persisted = true;
                }

                await _portService.SendPacketAsync(port.Id, packet).ConfigureAwait(false);

                sent++;
                _logger.LogInformation(
                    "Sent APRS message to {Addressee} on port {PortName} (msg#{MessageNumber}, retry {RetryCount})",
                    message.Peer,
                    port.Name,
                    message.MessageNumber,
                    message.RetryCount);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                _logger.LogError(ex, "Failed to send APRS message on port {PortName}", port.Name);
            }
        }

        if (!persisted && lastError is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(lastError);
        }

        if (sent == 0 && lastError != null)
        {
            _logger.LogError(lastError, "Failed to send APRS message to {Addressee}", message.Peer);
        }
    }

    private async Task SendAckAsync(AprsAddress addressee, int messageNumber, Guid? portId)
    {
        string baseCallsign = _configurationService.Current.Aprs.Callsign;
        string callsignString = _portSettingsResolver.GetCallsign(baseCallsign);

        MessageAckPacket packet = _packetFactory.CreateAckPacket(callsignString, addressee, messageNumber);

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
                List<PortConfig> txPorts = [.. _portService.Ports.Where(p => p.IsEnabled && p.IsTx)];
                foreach (PortConfig port in txPorts)
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
        string ownBase = _configurationService.Current.Aprs.Callsign.Trim().ToUpperInvariant();

        if (e.Packet is MessagePacket message)
        {
            bool addressedToUs = string.Equals(message.Addressee.Base, ownBase, StringComparison.Ordinal);
            bool fromUs = string.Equals(message.Source.Base, ownBase, StringComparison.Ordinal);

            if (addressedToUs && !fromUs)
            {
                HandleIncomingMessage(message, e.PortId);
            }
        }
        else if (e.Packet is MessageAckPacket ack)
        {
            bool addressedToUs = string.Equals(ack.Addressee.Base, ownBase, StringComparison.Ordinal);
            if (addressedToUs)
            {
                HandleAck(ack);
            }
        }
        else if (e.Packet is MessageRejPacket rej)
        {
            bool addressedToUs = string.Equals(rej.Addressee.Base, ownBase, StringComparison.Ordinal);
            if (addressedToUs)
            {
                HandleRej(rej);
            }
        }
    }

    private void HandleIncomingMessage(MessagePacket message, Guid portId)
    {
        // Deduplicate messages: RF?APRS-IS gating means the same message may arrive
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

        // For incoming messages, the peer is the source station (converted to AprsAddress)
        AprsAddress peer = AprsAddress.FromCallsign(message.Source);
        StoredMessage stored = new()
        {
            Peer = peer,
            Text = message.Text,
            Timestamp = message.Timestamp ?? _timeProvider.GetUtcNow(),
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
                ConversationThread thread = GetOrCreateConversationUnlocked(peer);
                thread.Add(stored);
                MoveConversationToFront(thread);
            }
        });

        NotifyConversationsChanged();

        // Post a notification unless the user is already viewing this peer's
        // conversation. Fire-and-forget: notifications must never break message
        // ingestion, so any error is logged and swallowed.
        if (_notificationService is not null)
        {
            AprsAddress? activePeer = _activeConversationTracker?.Peer;
            if (activePeer is null || !activePeer.Equals(peer))
            {
                _ = ShowMessageNotificationAsync(peer, message.Text);
            }
        }

        // Auto-send ACK if enabled and message has a number
        if (_configurationService.Current.Aprs.AutoAcknowledgeMessages && message.MessageNumber.HasValue)
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
            // Convert ack.Source (Callsign) to AprsAddress for comparison
            AprsAddress ackSource = AprsAddress.FromCallsign(ack.Source);
            if (!ackSource.Equals(message.Peer))
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

            // Convert rej.Source (Callsign) to AprsAddress for comparison
            AprsAddress rejSource = AprsAddress.FromCallsign(rej.Source);
            if (!rejSource.Equals(message.Peer))
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

        DateTimeOffset now = _timeProvider.GetUtcNow();
        AprsSettings settings = _configurationService.Current.Aprs;
        List<StoredMessage> toRetry = [];
        List<StoredMessage> timedOut = [];

        lock (_gate)
        {
            foreach (KeyValuePair<int, StoredMessage> kvp in _pendingMessages.ToList())
            {
                if (_disposed)
                {
                    return;
                }

                StoredMessage message = kvp.Value;
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
                        double backoff = settings.MessageRetryTimeoutSeconds * Math.Pow(2, message.RetryCount - 1);
                        message.NextRetryTime = now.AddSeconds(backoff);
                        toRetry.Add(message);
                    }
                }
            }
        }

        // Persist and retry outside the lock.
        foreach (StoredMessage message in timedOut)
        {
            PersistMessageStatusInBackground(message);
        }

        foreach (StoredMessage message in toRetry)
        {
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
        Interlocked.Increment(ref _pendingPersists);
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
            finally
            {
                Interlocked.Decrement(ref _pendingPersists);
                SignalPersistIdle();
            }
        });
    }

    private static TaskCompletionSource CreatePersistSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void SignalPersistIdle()
    {
        TaskCompletionSource signal;
        lock (_persistLock)
        {
            signal = _persistSignal;
            _persistSignal = CreatePersistSignal();
        }

        signal.TrySetResult();
    }

    private async Task UpdateMessageStatusAsync(StoredMessage message)
    {
        if (message.MessageNumber is not { } messageNumber || message.DeliveryStatus is not { } deliveryStatus)
        {
            return;
        }

        await _packetStorageService.UpdateOutboundMessageStatusAsync(
            message.Peer,
            messageNumber,
            deliveryStatus,
            message.RetryCount,
            message.NextRetryTime).ConfigureAwait(false);
    }

    private ConversationThread GetOrCreateConversationUnlocked(AprsAddress peer)
    {
        ConversationThread? existing = _conversationList.FirstOrDefault(c => c.Peer.Equals(peer));
        if (existing != null)
        {
            return existing;
        }

        ConversationThread created = new(peer);
        _conversationList.Insert(0, created);
        return created;
    }

    private void MoveConversationToFront(ConversationThread thread)
    {
        int index = _conversationList.IndexOf(thread);
        if (index > 0)
        {
            _conversationList.RemoveAt(index);
            _conversationList.Insert(0, thread);
        }
    }

    private void NotifyConversationsChanged() =>
        RunOnUi(() => ConversationsChanged?.Invoke(this, EventArgs.Empty));

    private void RunOnUi(Action action) => _uiDispatcher.Post(action);

    private async Task ShowMessageNotificationAsync(AprsAddress peer, string text)
    {
        try
        {
            await _notificationService!.ShowMessageNotificationAsync(
                peer.ToString(),
                text,
                peer.ToString()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post message notification for {Peer}", peer);
        }
    }

    /// <summary>
    /// Records the message in the dedup table and returns whether it was already seen
    /// within the retention window.
    /// </summary>
    private bool IsDuplicateMessage(Callsign source, int messageNumber)
    {
        string key = $"{source}|{messageNumber}";
        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (_dedupLock)
        {
            if (_dedupTable.TryGetValue(key, out DateTimeOffset seen) && now - seen < DedupRetention)
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
                (string? evictedKey, DateTimeOffset _) = _dedupOrder.Dequeue();
                _dedupTable.Remove(evictedKey);
            }

            return false;
        }
    }

    private async Task LoadMessagesAsync(CancellationToken cancellationToken = default)
    {
        if (_packetQueryService is null)
        {
            return;
        }

        IReadOnlyList<PacketRecord> records = await _packetQueryService
            .GetMessagePacketsAsync(MaxLoadedMessages, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, ConversationThread> threadsByPeer = [];
        int maxMessageNumber = await _packetQueryService
            .GetHighestOutboundMessageNumberAsync(cancellationToken).ConfigureAwait(false) ?? 1;

        foreach (PacketRecord? record in records.OrderBy(p => p.Timestamp).ThenBy(p => p.Id))
        {
            if (record.Message == null)
            {
                continue;
            }

            AprsAddress source = AprsAddress.FromCallsign(new Callsign(record.SourceBase, (int)record.SourceSsid));
            AprsAddress addressee = AprsAddress.FromStored(record.Message.AddresseeBase, (int)record.Message.AddresseeSsid);

            // Determine peer (who we're talking to)
            AprsAddress peer = record.IsOutbound ? addressee : source;

            StoredMessage stored = new()
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

            if (!threadsByPeer.TryGetValue(peer.ToString(), out ConversationThread? thread))
            {
                thread = new ConversationThread(peer);
                threadsByPeer[peer.ToString()] = thread;
            }

            thread.Add(stored);

            // Re-add pending messages to retry queue
            if (stored.IsOutbound &&
                stored.MessageNumber.HasValue &&
                stored.DeliveryStatus == MessageDeliveryStatus.Pending)
            {
                stored.NextRetryTime ??= stored.Timestamp.AddSeconds(
                    _configurationService.Current.Aprs.MessageRetryTimeoutSeconds);
                _pendingMessages[stored.MessageNumber.Value] = stored;
            }

            if (stored.MessageNumber is { } number && number > maxMessageNumber)
            {
                maxMessageNumber = number;
            }
        }

        lock (_gate)
        {
            foreach (ConversationThread? thread in threadsByPeer.Values.OrderByDescending(thread => thread.LastTimestamp))
            {
                _conversationList.Add(thread);
            }

            _nextMessageNumber = maxMessageNumber + 1;
        }

        NotifyConversationsChanged();
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

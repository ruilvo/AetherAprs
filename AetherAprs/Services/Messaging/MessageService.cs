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
using AetherAprs.Services.Concurrency;
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
    private readonly IMessageCommands _messageCommands;
    private readonly IMessageQueries _messageQueries;
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
    private readonly Dictionary<Guid, StoredMessage> _queuedMessages = [];
    private readonly Lock _dedupLock = new();
    private readonly Queue<(string Key, DateTimeOffset Seen)> _dedupOrder = new();
    private readonly Dictionary<string, DateTimeOffset> _dedupTable = [];
    private readonly SerializedWorkQueue _outboundQueue;
    private readonly ITimer _retryTimer;
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
    /// Completes once every background action queued before this call has finished,
    /// including retry passes, delivery outcomes and status writes. Lets tests assert
    /// the durable state without polling the database.
    /// </summary>
    internal Task FlushAsync(CancellationToken cancellationToken = default) =>
        _outboundQueue.WhenIdleAsync(cancellationToken);

    public MessageService(
        IPortService portService,
        IPacketStorageService packetStorageService,
        IConfigurationService configurationService,
        IAprsPortSettingsResolver portSettingsResolver,
        IPacketFactory packetFactory,
        ILogger<MessageService> logger,
        IUiDispatcher uiDispatcher,
        IMessageCommands messageCommands,
        IMessageQueries messageQueries,
        IPacketQueryService? packetQueryService = null,
        INotificationService? notificationService = null,
        TimeProvider? timeProvider = null,
        IActiveConversationTracker? activeConversationTracker = null)
    {
        _portService = portService;
        _packetStorageService = packetStorageService;
        _messageCommands = messageCommands;
        _messageQueries = messageQueries;
        _configurationService = configurationService;
        _portSettingsResolver = portSettingsResolver;
        _packetFactory = packetFactory;
        _logger = logger;
        _uiDispatcher = uiDispatcher;
        _packetQueryService = packetQueryService;
        _notificationService = notificationService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _activeConversationTracker = activeConversationTracker;
        _outboundQueue = new SerializedWorkQueue("message-outbound", logger);
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

    /// <inheritdoc />
    public async Task SendAsync(AprsAddress addressee, string text, CancellationToken cancellationToken = default)
        => await SendAsync(addressee, text, Guid.CreateVersion7(), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Sends one logical message identified by the caller so an enqueue retry, a
    /// concurrent caller or a restart resolves to the same durable record.
    /// </summary>
    public async Task<Guid> SendAsync(
        AprsAddress addressee,
        string text,
        Guid clientMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        text = text.Trim();
        if (text.Length > AprsMessageTextMaxLength)
        {
            throw new ArgumentException("APRS message text must be 67 characters or fewer.", nameof(text));
        }

        AprsSettings settings = _configurationService.Current.Aprs;
        DateTimeOffset timestamp = _timeProvider.GetUtcNow();
        TimeSpan retryInterval = TimeSpan.FromSeconds(settings.MessageRetryTimeoutSeconds);

        // Durable first: the record exists before the wire is used, and a second
        // caller with the same identity joins the existing record instead of
        // creating a second message or reusing its number.
        OutboxEnqueueResult enqueued = await _messageCommands.EnqueueAsync(
            new OutboxMessageDraft(
                clientMessageId,
                addressee,
                text,
                timestamp,
                timestamp.Add(GetExpiryWindow(settings)),
                PortId: null,
                timestamp.Add(retryInterval)),
            cancellationToken).ConfigureAwait(false);

        if (!enqueued.IsNew)
        {
            _logger.LogInformation(
                "Outbound message {MessageId} is already durable with status {Status}",
                enqueued.Id,
                enqueued.Status);
            return enqueued.Id;
        }

        StoredMessage stored = new()
        {
            LogicalId = enqueued.Id,
            Peer = addressee,
            Text = text,
            Timestamp = timestamp,
            IsOutbound = true,
            MessageNumber = enqueued.MessageNumber,
            DeliveryStatus = enqueued.Status,
            RetryCount = 0,
            MaxRetries = settings.MessageMaxRetries,
            NextRetryTime = enqueued.NextAttemptAt
        };

        if (enqueued.MessageNumber is not { } messageNumber)
        {
            // Every wire number is still reserved by an active or guarded message.
            // Wait in a queued state rather than reusing one of them.
            lock (_gate)
            {
                _queuedMessages[enqueued.Id] = stored;
            }

            PublishConversation(addressee, stored);
            return enqueued.Id;
        }

        lock (_gate)
        {
            _pendingMessages[messageNumber] = stored;
        }

        DateTimeOffset nextAttemptAt = enqueued.NextAttemptAt ?? timestamp.Add(retryInterval);

        try
        {
            await SendMessagePacketAsync(stored, retryCount: 0, nextAttemptAt, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                stored.NextRetryTime = _timeProvider.GetUtcNow().Add(retryInterval);
            }

            throw;
        }

        PublishConversation(addressee, stored);
        return enqueued.Id;
    }

    /// <summary>
    /// Transmits one attempt of a logical message.
    /// </summary>
    /// <param name="message">The message being transmitted.</param>
    /// <param name="retryCount">
    /// How many retries this attempt follows. It is passed in rather than read from
    /// <paramref name="message"/> so a burst of virtual-time retries cannot rewrite
    /// the value an earlier attempt is still using.
    /// </param>
    /// <param name="nextAttemptAt">When the next attempt is due, recorded with the attempt.</param>
    /// <param name="cancellationToken">Abandons the attempt.</param>
    private async Task SendMessagePacketAsync(
        StoredMessage message,
        int retryCount,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        List<PortConfig> txPorts = [.. _portService.Ports.Where(p => p.IsEnabled && p.IsTx)];
        if (txPorts.Count == 0)
        {
            // No route is not a terminal failure: the durable record stays pending
            // and the retry loop picks it up as soon as a TX port is available.
            lock (_gate)
            {
                message.NoRoute = true;
                message.NextRetryTime = _timeProvider.GetUtcNow().Add(GetBackoff(Math.Max(1, retryCount + 1)));
            }

            NotifyConversationUpdated(message.Peer);
            _logger.LogWarning(
                "No enabled TX port is available; message #{MessageNumber} to {Peer} stays pending",
                message.MessageNumber,
                message.Peer);
            return;
        }

        if (!await RecordAttemptAsync(message, nextAttemptAt, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        string baseCallsign = _configurationService.Current.Aprs.Callsign;
        Exception? lastError = null;
        int sent = 0;
        bool persisted = false;

        MarkTransmitting(message);
        try
        {
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

                    // One history row per logical message: persist on the first
                    // attempt, then only update it from the retry/status path.
                    if (!persisted)
                    {
                        await _packetStorageService
                            .EnsureOutboundMessageAsync(
                                packet,
                                port.Id,
                                retryCount,
                                nextAttemptAt,
                                message.LogicalId,
                                cancellationToken)
                            .ConfigureAwait(false);
                        persisted = true;
                    }

                    await _portService.SendPacketAsync(port.Id, packet).ConfigureAwait(false);

                    sent++;
                    lock (_gate)
                    {
                        message.RouteName = port.Name;
                        message.NoRoute = false;
                    }

                    _logger.LogInformation(
                        "Sent APRS message to {Addressee} on port {PortName} (msg#{MessageNumber}, attempt {Attempt})",
                        message.Peer,
                        port.Name,
                        message.MessageNumber,
                        retryCount + 1);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    lastError = ex;
                    _logger.LogError(ex, "Failed to send APRS message on port {PortName}", port.Name);
                }
            }
        }
        finally
        {
            ClearTransmitting(message);
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

    /// <summary>
    /// Shows the in-flight attempt while the port writes, without touching the
    /// durable status: the outbox row stays pending until an outcome arrives.
    /// </summary>
    private void MarkTransmitting(StoredMessage message)
    {
        lock (_gate)
        {
            // Inbound messages have no delivery lifecycle, so a null status is a no-op.
            if (message.DeliveryStatus is { } current
                && MessageDelivery.TryFire(current, DeliveryTrigger.BeginAttempt, out MessageDeliveryStatus next))
            {
                message.DeliveryStatus = next;
            }
        }

        NotifyConversationUpdated(message.Peer);
    }

    private void ClearTransmitting(StoredMessage message)
    {
        lock (_gate)
        {
            if (message.DeliveryStatus is { } current
                && MessageDelivery.TryFire(current, DeliveryTrigger.EndAttempt, out MessageDeliveryStatus next))
            {
                message.DeliveryStatus = next;
            }
        }

        NotifyConversationUpdated(message.Peer);
    }

    /// <summary>
    /// Takes the durable outcome as the cache's new status.
    /// </summary>
    /// <param name="current">The cached status, or <see langword="null"/> for inbound messages.</param>
    /// <param name="outcome">The terminal status the durable record now holds.</param>
    /// <returns>
    /// The state the transition table reaches from <paramref name="current"/>, or
    /// <paramref name="outcome"/> when the cache no longer accepts it. The durable
    /// record owns the outcome, so a stale cache is refreshed rather than kept.
    /// </returns>
    private static MessageDeliveryStatus AdoptOutcome(
        MessageDeliveryStatus? current,
        MessageDeliveryStatus outcome) =>
        current is { } status
            && MessageDelivery.TryFire(status, MessageDelivery.TriggerForOutcome(outcome), out MessageDeliveryStatus next)
                ? next
                : outcome;

    /// <summary>
    /// Counts the attempt on the durable record before the wire is used. A record
    /// that is already terminal (for example ACKed by a concurrent path) stops the
    /// transmission instead of resurrecting it.
    /// </summary>
    private async Task<bool> RecordAttemptAsync(
        StoredMessage message,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken)
    {
        if (message.LogicalId is not { } logicalId)
        {
            return true;
        }

        OutboxTransitionResult recorded = await _messageCommands
            .RecordAttemptAsync(logicalId, nextAttemptAt, cancellationToken)
            .ConfigureAwait(false);
        if (recorded.Outcome != OutboxTransitionOutcome.Applied)
        {
            _logger.LogInformation(
                "Outbound message {MessageId} is no longer open ({Outcome}); skipping transmission",
                logicalId,
                recorded.Outcome);
            return false;
        }

        return true;
    }

    private TimeSpan GetBackoff(int retryNumber)
    {
        int seconds = _configurationService.Current.Aprs.MessageRetryTimeoutSeconds;
        return TimeSpan.FromSeconds(seconds * Math.Pow(2, Math.Max(0, retryNumber - 1)));
    }

    /// <summary>
    /// Outbound messages expire after the operator's window, so queued work never
    /// survives indefinitely and a restart only restores unexpired messages.
    /// </summary>
    private static TimeSpan GetExpiryWindow(AprsSettings settings)
        => TimeSpan.FromMinutes(Math.Max(1, settings.MessageExpiryMinutes));

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
                HandleDeliveryOutcome(ack.Source, ack.MessageNumber, MessageDeliveryStatus.Acknowledged);
            }
        }
        else if (e.Packet is MessageRejPacket rej)
        {
            bool addressedToUs = string.Equals(rej.Addressee.Base, ownBase, StringComparison.Ordinal);
            if (addressedToUs)
            {
                HandleDeliveryOutcome(rej.Source, rej.MessageNumber, MessageDeliveryStatus.Rejected);
            }
        }
    }

    private void HandleIncomingMessage(MessagePacket message, Guid portId)
    {
        // For incoming messages, the peer is the source station (converted to AprsAddress)
        AprsAddress peer = AprsAddress.FromCallsign(message.Source);

        // Deduplicate messages: RF/APRS-IS gating means the same message may arrive
        // on multiple ports within seconds. Track recently seen (source, number) pairs.
        if (message.MessageNumber.HasValue && IsDuplicateMessage(message.Source, message.MessageNumber.Value))
        {
            // The copy is folded into the existing message so the port count keeps
            // showing every route that carried it.
            RecordDuplicateRoute(peer, message.MessageNumber.Value, portId);
            _logger.LogDebug(
                "Suppressing duplicate message #{MessageNumber} from {Source} on port {PortId}",
                message.MessageNumber.Value,
                message.Source,
                portId);
            return;
        }

        StoredMessage stored = new()
        {
            Peer = peer,
            Text = message.Text,
            Timestamp = message.Timestamp ?? _timeProvider.GetUtcNow(),
            IsOutbound = false,
            MessageNumber = message.MessageNumber,
            PortId = portId
        };
        stored.ReceivedPortIds.Add(portId);

        _logger.LogInformation(
            "Received APRS message from {Peer} on port {PortId} with number {MessageNumber}",
            peer,
            portId,
            message.MessageNumber);

        PublishConversation(peer, stored);

        // Post a notification unless the user is already viewing this peer's
        // conversation. Fire-and-forget: notifications must never break message
        // ingestion, so any error is logged and swallowed.
        if (_notificationService is not null)
        {
            AprsAddress? activePeer = _activeConversationTracker?.Peer;
            if (activePeer is null || !activePeer.Equals(peer))
            {
                _outboundQueue.Enqueue(_ => ShowMessageNotificationAsync(peer, message.Text));
            }
        }

        // Auto-send ACK if enabled and message has a number
        if (_configurationService.Current.Aprs.AutoAcknowledgeMessages && message.MessageNumber.HasValue)
        {
            _outboundQueue.Enqueue(_ => SendAckAsync(peer, message.MessageNumber.Value, portId));
        }
    }

    /// <summary>
    /// Applies a terminal delivery outcome. The durable transition decides whether
    /// the outcome is still relevant: a duplicate or late ACK/REJ finds no open
    /// record and can never regress a terminal message or a reused number.
    /// </summary>
    private void HandleDeliveryOutcome(Callsign source, int messageNumber, MessageDeliveryStatus outcome)
    {
        AprsAddress peer = AprsAddress.FromCallsign(source);
        StoredMessage? cached;
        lock (_gate)
        {
            if (!_pendingMessages.TryGetValue(messageNumber, out cached))
            {
                cached = null;
            }
            else if (!cached.Peer.Equals(peer))
            {
                // Only the message's addressee may acknowledge it; small integer
                // message numbers collide easily between stations.
                _logger.LogWarning(
                    "Ignoring {Outcome} from {Source} for message #{MessageNumber} addressed to {Peer}",
                    outcome,
                    source,
                    messageNumber,
                    cached.Peer);
                return;
            }
        }

        _logger.LogInformation(
            "Received {Outcome} from {Source} for message #{MessageNumber}",
            outcome,
            source,
            messageNumber);
        _outboundQueue.Enqueue(_ => ApplyDeliveryOutcomeAsync(peer, messageNumber, outcome, cached));
    }

    private async Task ApplyDeliveryOutcomeAsync(
        AprsAddress peer,
        int messageNumber,
        MessageDeliveryStatus outcome,
        StoredMessage? cached)
    {
        try
        {
            OutboxTransitionResult result = await _messageCommands
                .ApplyDeliveryOutcomeAsync(peer, messageNumber, outcome)
                .ConfigureAwait(false);
            if (result.Outcome != OutboxTransitionOutcome.Applied)
            {
                _logger.LogInformation(
                    "Ignoring {Outcome} for message #{MessageNumber} to {Peer}: {Reason}",
                    outcome,
                    messageNumber,
                    peer,
                    result.Outcome);
                return;
            }

            StoredMessage? message = cached;
            lock (_gate)
            {
                if (_pendingMessages.TryGetValue(messageNumber, out StoredMessage? tracked))
                {
                    message ??= tracked;
                    _pendingMessages.Remove(messageNumber);
                }
            }

            if (message is not null)
            {
                // The durable record already holds the outcome and this object only
                // caches it for the conversation list, so a cache that no longer
                // accepts the transition is refreshed from the record instead of
                // being allowed to keep a stale status.
                message.DeliveryStatus = AdoptOutcome(message.DeliveryStatus, outcome);
                PersistMessageStatusInBackground(message);
            }

            NotifyConversationUpdated(peer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply {Outcome} for message #{MessageNumber}", outcome, messageNumber);
        }
    }

    /// <summary>
    /// Schedules every message whose retry is due. Internal so a test can drive one
    /// pass without waiting for the timer.
    /// </summary>
    /// <remarks>
    /// Scheduling is synchronous, so a virtual clock advances the retry state machine
    /// deterministically. Only transmission and persistence leave the tick, and they
    /// run one at a time on <see cref="_outboundQueue"/>: a burst of ticks can no
    /// longer start concurrent passes that race each other over the same message.
    /// </remarks>
    internal void ProcessRetries(object? state) // internal for tests (InternalsVisibleTo AetherAprs.Tests)
    {
        if (_disposed)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        AprsSettings settings = _configurationService.Current.Aprs;
        List<RetryAttempt> due = [];
        List<StoredMessage> timedOut = [];
        bool hasQueued;

        lock (_gate)
        {
            foreach (KeyValuePair<int, StoredMessage> kvp in _pendingMessages.ToList())
            {
                StoredMessage message = kvp.Value;
                if (!message.NextRetryTime.HasValue || message.NextRetryTime.Value > now)
                {
                    continue;
                }

                if (message.RetryCount >= settings.MessageMaxRetries)
                {
                    // Only a pending or in-flight message is tracked here, so an
                    // illegal timeout means the message already resolved. Leave its
                    // status alone - recording a timeout over an acknowledgment would
                    // mislabel it - and stop tracking it.
                    if (message.DeliveryStatus is not { } current
                        || !MessageDelivery.TryFire(current, DeliveryTrigger.Timeout, out MessageDeliveryStatus timedOutStatus))
                    {
                        _pendingMessages.Remove(kvp.Key);
                        _logger.LogDebug(
                            "Message #{MessageNumber} to {Peer} stopped retrying from {State}",
                            message.MessageNumber,
                            message.Peer,
                            message.DeliveryStatus);
                        continue;
                    }

                    message.DeliveryStatus = timedOutStatus;
                    message.NextRetryTime = null;
                    _pendingMessages.Remove(kvp.Key);
                    timedOut.Add(message);
                    _logger.LogWarning(
                        "Message #{MessageNumber} to {Peer} timed out after {RetryCount} retries",
                        message.MessageNumber,
                        message.Peer,
                        message.RetryCount);
                    continue;
                }

                message.RetryCount++;
                DateTimeOffset nextAttemptAt = now.Add(GetBackoff(message.RetryCount));
                message.NextRetryTime = nextAttemptAt;
                due.Add(new RetryAttempt(message, message.RetryCount, nextAttemptAt));
            }

            hasQueued = _queuedMessages.Count > 0;
        }

        if (due.Count == 0 && timedOut.Count == 0 && !hasQueued)
        {
            // Nothing is waiting on the outbox, so there is nothing to expire or promote.
            return;
        }

        _outboundQueue.Enqueue(async cancellationToken =>
        {
            foreach (RetryAttempt attempt in due)
            {
                try
                {
                    await SendMessagePacketAsync(
                        attempt.Message,
                        attempt.RetryCount,
                        attempt.NextAttemptAt,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(
                        ex,
                        "Error retrying message #{MessageNumber}",
                        attempt.Message.MessageNumber);
                }
            }

            foreach (StoredMessage message in timedOut)
            {
                await CompleteAsync(message).ConfigureAwait(false);
            }

            try
            {
                await ExpireOutboxAsync(now).ConfigureAwait(false);
                await PromoteQueuedMessagesAsync(now, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to process outbound message retries");
            }
        });
    }

    /// <summary>One scheduled retry, with the values that belong to that attempt.</summary>
    private sealed record RetryAttempt(StoredMessage Message, int RetryCount, DateTimeOffset NextAttemptAt);

    /// <summary>Marks expired durable work so it is no longer replayed after a restart.</summary>
    /// <param name="asOf">
    /// The instant the retry pass acted for. Using the pass's instant instead of the
    /// clock keeps a queued sweep from observing a later time than the pass that
    /// scheduled it, which would expire messages the pass was still retrying.
    /// </param>
    private async Task ExpireOutboxAsync(DateTimeOffset asOf)
    {
        IReadOnlyList<OutboxMessageSnapshot> expired = await _messageCommands
            .ExpireAsync(asOf)
            .ConfigureAwait(false);
        foreach (OutboxMessageSnapshot snapshot in expired)
        {
            StoredMessage? message = null;
            lock (_gate)
            {
                if (snapshot.MessageNumber is { } number
                    && _pendingMessages.TryGetValue(number, out StoredMessage? pending)
                    && pending.LogicalId == snapshot.Id)
                {
                    message = pending;
                    _pendingMessages.Remove(number);
                }
                else if (_queuedMessages.Remove(snapshot.Id, out StoredMessage? queued))
                {
                    message = queued;
                }
            }

            if (message is not null)
            {
                // The durable sweep already expired the row, so this cache entry
                // follows it.
                message.DeliveryStatus = AdoptOutcome(message.DeliveryStatus, MessageDeliveryStatus.Expired);
                message.NextRetryTime = null;
                PersistMessageStatusInBackground(message);
                NotifyConversationUpdated(message.Peer);
            }
        }
    }

    /// <summary>Gives queued messages a wire number as soon as one is free again.</summary>
    /// <param name="asOf">The instant the retry pass acted for.</param>
    /// <param name="cancellationToken">Abandons the pass.</param>
    private async Task PromoteQueuedMessagesAsync(DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        List<StoredMessage> queued;
        lock (_gate)
        {
            queued = [.. _queuedMessages.Values];
        }

        foreach (StoredMessage message in queued)
        {
            if (_disposed || message.LogicalId is not { } logicalId)
            {
                continue;
            }

            try
            {
                OutboxTransitionResult reserved = await _messageCommands
                    .TryReserveNumberAsync(
                        logicalId,
                        message.NextRetryTime ?? asOf,
                        asOf,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (reserved.Outcome != OutboxTransitionOutcome.Applied
                    || reserved.Message is not { MessageNumber: { } number } snapshot)
                {
                    continue;
                }

                lock (_gate)
                {
                    _queuedMessages.Remove(logicalId);
                    message.MessageNumber = number;
                    message.DeliveryStatus = message.DeliveryStatus is { } current
                        ? MessageDelivery.Fire(current, DeliveryTrigger.ReserveWireNumber)
                        : MessageDeliveryStatus.Pending;
                    message.NextRetryTime = snapshot.NextAttemptAt;
                    _pendingMessages[number] = message;
                }

                await SendMessagePacketAsync(
                    message,
                    message.RetryCount,
                    snapshot.NextAttemptAt ?? asOf,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to promote queued message {MessageId}", logicalId);
            }
        }
    }

    /// <summary>Persists the terminal path: ACK/REJ/timeout/expiry share one transition.</summary>
    private async Task CompleteAsync(StoredMessage message)
    {
        if (message.LogicalId is { } logicalId && message.MessageNumber is { } number)
        {
            OutboxTransitionResult result = await _messageCommands
                .ApplyDeliveryOutcomeAsync(message.Peer, number, message.DeliveryStatus ?? MessageDeliveryStatus.Failed)
                .ConfigureAwait(false);
            if (result.Outcome == OutboxTransitionOutcome.AlreadyTerminal)
            {
                _logger.LogInformation("Message {MessageId} was already terminal", logicalId);
            }
        }

        PersistMessageStatusInBackground(message);
        NotifyConversationUpdated(message.Peer);
    }

    /// <summary>
    /// Queues a status write behind the same serialized queue as every other
    /// background action, so a status change can never race the transmission that
    /// produced it. The status is read when the write runs, which is safe because a
    /// status only moves forward after the message leaves
    /// <see cref="_pendingMessages"/>.
    /// </summary>
    private void PersistMessageStatusInBackground(StoredMessage message) =>
        _outboundQueue.Enqueue(async cancellationToken =>
        {
            try
            {
                await UpdateMessageStatusAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to persist message status for {Peer}", message.Peer);
            }
        });

    private async Task UpdateMessageStatusAsync(StoredMessage message, CancellationToken cancellationToken)
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
            message.NextRetryTime,
            cancellationToken).ConfigureAwait(false);
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

    private void PublishConversation(AprsAddress peer, StoredMessage stored)
    {
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
    }

    private void NotifyConversationsChanged() =>
        RunOnUi(() => ConversationsChanged?.Invoke(this, EventArgs.Empty));

    /// <summary>
    /// Tells the owning thread that one of its messages changed in place, so a
    /// visible conversation re-reads the delivery state instead of only the list.
    /// </summary>
    private void NotifyConversationUpdated(AprsAddress peer)
    {
        RunOnUi(() =>
        {
            lock (_gate)
            {
                ConversationThread? thread = _conversationList.FirstOrDefault(c => c.Peer.Equals(peer));
                thread?.NotifyMessageUpdated();
            }
        });

        NotifyConversationsChanged();
    }

    private void RunOnUi(Action action) => _uiDispatcher.Post(action);

    private async Task ShowMessageNotificationAsync(AprsAddress peer, string text)
    {
        try
        {
            // The body stays private unless the operator opts in: lock-screen
            // previews would otherwise expose message text.
            string body = _configurationService.Current.Aprs.ShowMessageTextInNotifications
                ? text
                : Localization.Strings.Get("MessageNotificationHiddenBody");

            await _notificationService!.ShowMessageNotificationAsync(
                peer.ToString(),
                body,
                peer.ToString()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post message notification for {Peer}", peer);
        }
    }

    /// <summary>
    /// Folds a duplicate copy into the message that already exists so the port
    /// count reflects every route that carried it.
    /// </summary>
    private void RecordDuplicateRoute(AprsAddress peer, int messageNumber, Guid portId)
    {
        bool changed = false;
        lock (_gate)
        {
            ConversationThread? thread = _conversationList.FirstOrDefault(c => c.Peer.Equals(peer));
            StoredMessage? existing = thread?.Messages.LastOrDefault(
                message => !message.IsOutbound && message.MessageNumber == messageNumber);
            if (existing is not null && !existing.ReceivedPortIds.Contains(portId))
            {
                existing.ReceivedPortIds.Add(portId);
                changed = true;
            }
        }

        if (changed)
        {
            NotifyConversationUpdated(peer);
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
        // Expire first: durable work past its deadline must never be replayed.
        await _messageCommands.ExpireAsync(_timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        IReadOnlyList<OutboxMessageSnapshot> open = await _messageQueries
            .GetOpenAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<PacketRecord> records = _packetQueryService is null
            ? []
            : await _packetQueryService
                .GetMessagePacketsAsync(MaxLoadedMessages, cancellationToken)
                .ConfigureAwait(false);

        Dictionary<Guid, OutboxMessageSnapshot> openById = [];
        foreach (OutboxMessageSnapshot snapshot in open)
        {
            openById[snapshot.Id] = snapshot;
        }

        // Terminal durable rows are authoritative for history rows that are no
        // longer open (an ACK or expiry may have committed just before a crash).
        HashSet<Guid> terminalIds = [];
        foreach (PacketRecord record in records)
        {
            if (record.Message?.LogicalId is { } logicalId && !openById.ContainsKey(logicalId))
            {
                terminalIds.Add(logicalId);
            }
        }

        Dictionary<Guid, OutboxMessageSnapshot> terminalById = [];
        if (terminalIds.Count > 0)
        {
            foreach (OutboxMessageSnapshot snapshot in await _messageQueries
                .GetByIdsAsync(terminalIds, cancellationToken).ConfigureAwait(false))
            {
                terminalById[snapshot.Id] = snapshot;
            }
        }

        Dictionary<string, ConversationThread> threadsByPeer = [];
        HashSet<Guid> restored = [];
        List<StoredMessage> pending = [];
        List<StoredMessage> queued = [];
        List<StoredMessage> legacyExpired = [];
        List<StoredMessage> staleHistory = [];

        AprsSettings settings = _configurationService.Current.Aprs;
        bool hasTxRoute = _portService.Ports.Any(p => p.IsEnabled && p.IsTx);

        foreach (PacketRecord? record in records.OrderBy(p => p.Timestamp).ThenBy(p => p.Id))
        {
            if (record.Message == null)
            {
                continue;
            }

            AprsAddress source = AprsAddress.FromStored(record.SourceBase, (int)record.SourceSsid);
            AprsAddress addressee = AprsAddress.FromStored(record.Message.AddresseeBase, (int)record.Message.AddresseeSsid);

            // Determine peer (who we're talking to)
            AprsAddress peer = record.IsOutbound ? addressee : source;

            MessageDeliveryStatus? status = record.Message.DeliveryStatus;
            int retryCount = record.Message.RetryCount;
            DateTimeOffset? nextRetryTime = record.Message.NextRetryTime.HasValue
                ? new DateTimeOffset(record.Message.NextRetryTime.Value, TimeSpan.Zero)
                : null;

            if (record.IsOutbound && record.Message.LogicalId is { } logicalId)
            {
                if (openById.TryGetValue(logicalId, out OutboxMessageSnapshot? snapshot))
                {
                    status = snapshot.Status;
                    retryCount = Math.Max(0, snapshot.AttemptCount - 1);
                    nextRetryTime = snapshot.NextAttemptAt;
                    restored.Add(logicalId);
                }
                else if (terminalById.TryGetValue(logicalId, out OutboxMessageSnapshot? terminal))
                {
                    status = terminal.Status;
                    retryCount = Math.Max(0, terminal.AttemptCount - 1);
                    nextRetryTime = terminal.NextAttemptAt;
                }
            }
            else if (record.IsOutbound && status == MessageDeliveryStatus.Pending)
            {
                // A pending row without a logical identity predates the outbox. Its
                // ownership is unknown, so it is shown as expired and never replayed.
                status = MessageDeliveryStatus.Expired;
                nextRetryTime = null;
            }

            StoredMessage stored = new()
            {
                LogicalId = record.Message.LogicalId,
                Peer = peer,
                Text = record.Message.Text,
                Timestamp = new DateTimeOffset(record.Timestamp, TimeSpan.Zero),
                IsOutbound = record.IsOutbound,
                MessageNumber = record.Message.Number,
                PortId = record.PortId,
                RouteName = record.IsOutbound && record.PortId is { } routeId
                    ? _portService.Ports.FirstOrDefault(p => p.Id == routeId)?.Name
                    : null,
                DeliveryStatus = status,
                RetryCount = retryCount,
                MaxRetries = settings.MessageMaxRetries,
                NoRoute = record.IsOutbound && !hasTxRoute,
                NextRetryTime = nextRetryTime
            };

            AddToThread(threadsByPeer, peer, stored);

            if (record.IsOutbound
                && record.Message.DeliveryStatus == MessageDeliveryStatus.Pending
                && stored.DeliveryStatus != MessageDeliveryStatus.Pending)
            {
                staleHistory.Add(stored);
            }

            if (stored.IsOutbound
                && stored.LogicalId is { } id
                && stored.MessageNumber is { } number
                && stored.DeliveryStatus == MessageDeliveryStatus.Pending)
            {
                pending.Add(stored);
            }
            else if (stored.IsOutbound
                && stored.LogicalId is { } queuedId
                && stored.DeliveryStatus == MessageDeliveryStatus.Queued)
            {
                queued.Add(stored);
            }
            else if (record.IsOutbound && stored.DeliveryStatus == MessageDeliveryStatus.Expired
                && record.Message.LogicalId is null)
            {
                legacyExpired.Add(stored);
            }
        }

        // Durable records without a history row yet (queued, or killed before the
        // first wire attempt) still belong to the conversation set.
        foreach (OutboxMessageSnapshot snapshot in open)
        {
            if (restored.Contains(snapshot.Id))
            {
                continue;
            }

            StoredMessage stored = new()
            {
                LogicalId = snapshot.Id,
                Peer = snapshot.Addressee,
                Text = snapshot.Text,
                Timestamp = snapshot.CreatedAt,
                IsOutbound = true,
                MessageNumber = snapshot.MessageNumber,
                PortId = snapshot.PortId,
                RouteName = snapshot.PortId is { } openRouteId
                    ? _portService.Ports.FirstOrDefault(p => p.Id == openRouteId)?.Name
                    : null,
                DeliveryStatus = snapshot.Status,
                RetryCount = Math.Max(0, snapshot.AttemptCount - 1),
                MaxRetries = settings.MessageMaxRetries,
                NoRoute = !hasTxRoute,
                NextRetryTime = snapshot.NextAttemptAt
            };

            AddToThread(threadsByPeer, snapshot.Addressee, stored);

            if (stored.MessageNumber is { } number && stored.DeliveryStatus == MessageDeliveryStatus.Pending)
            {
                pending.Add(stored);
            }
            else if (stored.DeliveryStatus == MessageDeliveryStatus.Queued)
            {
                queued.Add(stored);
            }
        }

        lock (_gate)
        {
            foreach (ConversationThread? thread in threadsByPeer.Values.OrderByDescending(thread => thread.LastTimestamp))
            {
                _conversationList.Add(thread);
            }

            foreach (StoredMessage message in pending)
            {
                if (message.MessageNumber is { } number)
                {
                    _pendingMessages[number] = message;
                }
            }

            foreach (StoredMessage message in queued)
            {
                if (message.LogicalId is { } id)
                {
                    _queuedMessages[id] = message;
                }
            }
        }

        // Deterministic legacy merge: one expiry per (peer, number) pair, so a
        // duplicated historical row cannot resurrect the send.
        foreach (StoredMessage message in legacyExpired)
        {
            PersistMessageStatusInBackground(message);
        }

        // History rows whose durable row already reached a terminal state are
        // repaired on load instead of being displayed as pending.
        foreach (StoredMessage message in staleHistory)
        {
            PersistMessageStatusInBackground(message);
        }

        if (legacyExpired.Count > 0)
        {
            _logger.LogWarning(
                "Marked {Count} legacy pending outbound messages expired instead of replaying them",
                legacyExpired.Count);
        }

        NotifyConversationsChanged();
    }

    private static void AddToThread(
        Dictionary<string, ConversationThread> threadsByPeer,
        AprsAddress peer,
        StoredMessage stored)
    {
        string key = peer.ToString();
        if (!threadsByPeer.TryGetValue(key, out ConversationThread? thread))
        {
            thread = new ConversationThread(peer);
            threadsByPeer[key] = thread;
        }

        thread.Add(stored);
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
        _outboundQueue.Dispose();
    }
}

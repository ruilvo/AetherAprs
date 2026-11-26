// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Tests.Helpers;

/// <summary>
/// In-memory stand-in for the durable outbox. It mirrors the SQLite store's
/// contract (identity dedupe, number reservation, terminal transitions and the
/// reuse guard) so feature tests do not need a database file.
/// </summary>
internal sealed class FakeMessageOutbox : IMessageCommands, IMessageQueries
{
    private const int MaximumWireNumber = 9999;

    private readonly Lock _sync = new();
    private readonly Dictionary<Guid, Entry> _messages = [];
    private readonly TimeProvider _timeProvider;
    private readonly int _maximumWireNumber;

    public FakeMessageOutbox(TimeProvider? timeProvider = null, int maximumWireNumber = MaximumWireNumber)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maximumWireNumber = maximumWireNumber;
    }

    public event EventHandler<OutboxMessageChangedEventArgs>? MessageCommitted;

    internal static TimeSpan NumberReuseGuard => TimeSpan.FromMinutes(10);

    public Task<OutboxEnqueueResult> EnqueueAsync(OutboxMessageDraft draft, CancellationToken cancellationToken = default)
    {
        OutboxEnqueueResult result;
        lock (_sync)
        {
            if (_messages.TryGetValue(draft.Id, out Entry? existing))
            {
                return Task.FromResult(ToEnqueueResult(existing, isNew: false));
            }

            int? number = ReserveNumber(_timeProvider.GetUtcNow());
            Entry entry = new()
            {
                Id = draft.Id,
                Addressee = draft.Addressee,
                Text = draft.Text,
                MessageNumber = number,
                Status = number.HasValue ? MessageDeliveryStatus.Pending : MessageDeliveryStatus.Queued,
                CreatedAt = draft.CreatedAt,
                ExpiresAt = draft.ExpiresAt,
                NextAttemptAt = draft.NextAttemptAt,
                PortId = draft.PortId,
            };

            _messages[draft.Id] = entry;
            result = ToEnqueueResult(entry, isNew: true);
        }

        Raise(ToSnapshot(result.Id));
        return Task.FromResult(result);
    }

    public Task<OutboxTransitionResult> RecordAttemptAsync(
        Guid id,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_messages.TryGetValue(id, out Entry? entry))
            {
                return Task.FromResult(new OutboxTransitionResult(OutboxTransitionOutcome.UnknownMessage, null));
            }

            if (!IsOpen(entry.Status))
            {
                return Task.FromResult(new OutboxTransitionResult(OutboxTransitionOutcome.AlreadyTerminal, ToSnapshot(entry)));
            }

            entry.AttemptCount++;
            entry.NextAttemptAt = nextAttemptAt;
            Raise(ToSnapshot(entry));
            return Task.FromResult(new OutboxTransitionResult(OutboxTransitionOutcome.Applied, ToSnapshot(entry)));
        }
    }

    public Task<OutboxTransitionResult> TryReserveNumberAsync(
        Guid id,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_messages.TryGetValue(id, out Entry? entry))
            {
                return Task.FromResult(new OutboxTransitionResult(OutboxTransitionOutcome.UnknownMessage, null));
            }

            if (!IsOpen(entry.Status))
            {
                return Task.FromResult(new OutboxTransitionResult(OutboxTransitionOutcome.AlreadyTerminal, ToSnapshot(entry)));
            }

            if (!entry.MessageNumber.HasValue)
            {
                entry.MessageNumber = ReserveNumber(_timeProvider.GetUtcNow());
                if (entry.MessageNumber.HasValue)
                {
                    entry.Status = MessageDeliveryStatus.Pending;
                    entry.NextAttemptAt = nextAttemptAt;
                }
            }

            return Task.FromResult(new OutboxTransitionResult(OutboxTransitionOutcome.Applied, ToSnapshot(entry)));
        }
    }

    public Task<OutboxTransitionResult> ApplyDeliveryOutcomeAsync(
        AprsAddress addressee,
        int messageNumber,
        MessageDeliveryStatus outcome,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            List<Entry> matches = [.. _messages.Values.Where(message =>
                message.MessageNumber == messageNumber
                && message.Addressee.Equals(addressee))];
            if (matches.Count == 0)
            {
                return Task.FromResult(new OutboxTransitionResult(OutboxTransitionOutcome.UnknownMessage, null));
            }

            Entry? open = matches.FirstOrDefault(message => IsOpen(message.Status));
            if (open is null)
            {
                return Task.FromResult(new OutboxTransitionResult(
                    OutboxTransitionOutcome.AlreadyTerminal,
                    ToSnapshot(matches[0])));
            }

            Complete(open, outcome);
            Raise(ToSnapshot(open));
            return Task.FromResult(new OutboxTransitionResult(OutboxTransitionOutcome.Applied, ToSnapshot(open)));
        }
    }

    public Task<IReadOnlyList<OutboxMessageSnapshot>> ExpireAsync(CancellationToken cancellationToken = default)
    {
        List<OutboxMessageSnapshot> expired = [];
        lock (_sync)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (Entry entry in _messages.Values)
            {
                if (IsOpen(entry.Status) && entry.ExpiresAt <= now)
                {
                    Complete(entry, MessageDeliveryStatus.Expired);
                    expired.Add(ToSnapshot(entry));
                }
            }
        }

        foreach (OutboxMessageSnapshot snapshot in expired)
        {
            Raise(snapshot);
        }

        return Task.FromResult<IReadOnlyList<OutboxMessageSnapshot>>(expired);
    }

    public Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            List<Guid> removable = [.. _messages.Values
                .Where(entry => !IsOpen(entry.Status)
                    && entry.NumberReleaseAt.HasValue
                    && entry.NumberReleaseAt.Value <= now)
                .Select(entry => entry.Id)];
            foreach (Guid id in removable)
            {
                _messages.Remove(id);
            }

            return Task.FromResult(removable.Count);
        }
    }

    public Task<IReadOnlyList<OutboxMessageSnapshot>> GetOpenAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            return Task.FromResult<IReadOnlyList<OutboxMessageSnapshot>>([..
                _messages.Values
                    .Where(entry => IsOpen(entry.Status) && entry.ExpiresAt > now)
                    .OrderByDescending(entry => entry.CreatedAt)
                    .Select(ToSnapshot)]);
        }
    }

    public Task<IReadOnlyList<OutboxMessageSnapshot>> GetDueAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            return Task.FromResult<IReadOnlyList<OutboxMessageSnapshot>>([..
                _messages.Values
                    .Where(entry => IsOpen(entry.Status)
                        && entry.ExpiresAt > now
                        && entry.NextAttemptAt.HasValue
                        && entry.NextAttemptAt.Value <= now)
                    .OrderBy(entry => entry.CreatedAt)
                    .Select(ToSnapshot)]);
        }
    }

    public Task<OutboxMessageSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_messages.TryGetValue(id, out Entry? entry) ? ToSnapshot(entry) : null);
        }
    }

    public Task<IReadOnlyList<OutboxMessageSnapshot>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<OutboxMessageSnapshot>>([..
                ids.Where(_messages.ContainsKey).Select(id => ToSnapshot(_messages[id]))]);
        }
    }

    internal int Count => _messages.Count;

    private int? ReserveNumber(DateTimeOffset now)
    {
        HashSet<int> taken = [.. _messages.Values
            .Where(entry => entry.MessageNumber.HasValue
                && (entry.Status == MessageDeliveryStatus.Pending
                    || (entry.NumberReleaseAt.HasValue && entry.NumberReleaseAt.Value > now)))
            .Select(entry => entry.MessageNumber!.Value)];

        for (int candidate = 1; candidate <= _maximumWireNumber; candidate++)
        {
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private void Complete(Entry entry, MessageDeliveryStatus status)
    {
        entry.Status = status;
        entry.NextAttemptAt = null;
        entry.NumberReleaseAt = _timeProvider.GetUtcNow().Add(NumberReuseGuard);
    }

    private static bool IsOpen(MessageDeliveryStatus status) =>
        status is MessageDeliveryStatus.Pending or MessageDeliveryStatus.Queued;

    private static OutboxEnqueueResult ToEnqueueResult(Entry entry, bool isNew) => new(
        entry.Id, entry.MessageNumber, entry.Status, isNew, entry.AttemptCount, entry.NextAttemptAt);

    private static OutboxMessageSnapshot ToSnapshot(Entry entry) => new(
        entry.Id, entry.Addressee, entry.Text, entry.MessageNumber, entry.Status,
        entry.AttemptCount, entry.CreatedAt, entry.NextAttemptAt, entry.ExpiresAt, entry.PortId);

    private OutboxMessageSnapshot ToSnapshot(Guid id) =>
        ToSnapshot(_messages[id]);

    private void Raise(OutboxMessageSnapshot snapshot) =>
        MessageCommitted?.Invoke(this, new OutboxMessageChangedEventArgs(snapshot));

    private sealed class Entry
    {
        public required Guid Id { get; init; }
        public required AprsAddress Addressee { get; init; }
        public required string Text { get; init; }
        public int? MessageNumber { get; set; }
        public MessageDeliveryStatus Status { get; set; }
        public int AttemptCount { get; set; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public DateTimeOffset? NextAttemptAt { get; set; }
        public DateTimeOffset? NumberReleaseAt { get; set; }
        public Guid? PortId { get; init; }
    }
}

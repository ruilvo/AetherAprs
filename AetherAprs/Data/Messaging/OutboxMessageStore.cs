// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Data.Entities;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Messaging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Data.Messaging;

/// <summary>
/// The single owner of durable outbound message state. Writes are serialized by
/// the runtime writer; reads use short-lived read-only contexts. Subscribers are
/// notified after the commit and cannot fail the command that produced it.
/// </summary>
internal sealed class OutboxMessageStore(
    SqliteRuntimeWriter writer,
    SqliteRuntimeReader reader,
    TimeProvider timeProvider,
    ILogger<OutboxMessageStore> logger,
    int maximumWireNumber = 9999) : IMessageCommands, IMessageQueries
{
    /// <summary>Highest APRS message number this station will reserve.</summary>
    internal const int MaximumWireNumber = 9999;

    /// <summary>
    /// How long a terminal message keeps its wire number. A late ACK inside this
    /// window still resolves to the original message instead of a reused number.
    /// </summary>
    internal static readonly TimeSpan NumberReuseGuard = TimeSpan.FromMinutes(10);

    public event EventHandler<OutboxMessageChangedEventArgs>? MessageCommitted;

    public async Task<OutboxEnqueueResult> EnqueueAsync(OutboxMessageDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        DateTimeOffset now = timeProvider.GetUtcNow();
        (OutboxEnqueueResult Result, OutboxMessageRecord? Record) outcome = await writer.EnqueueAsync(
            "outbox.enqueue", async (context, token) =>
        {
            OutboxMessageRecord? existing = await context.OutboxMessages
                .FirstOrDefaultAsync(message => message.Id == draft.Id, token).ConfigureAwait(false);
            if (existing is not null)
            {
                return (ToEnqueueResult(existing, isNew: false), (OutboxMessageRecord?)null);
            }

            int? number = await ReserveNumberAsync(context, now, token).ConfigureAwait(false);
            OutboxMessageRecord record = new()
            {
                Id = draft.Id,
                AddresseeBase = draft.Addressee.Base,
                AddresseeSsid = ToStoredSsid(draft.Addressee),
                Text = draft.Text,
                MessageNumber = number,
                Status = number.HasValue ? MessageDeliveryStatus.Pending : MessageDeliveryStatus.Queued,
                AttemptCount = 0,
                CreatedAt = draft.CreatedAt.UtcDateTime,
                ExpiresAt = draft.ExpiresAt.UtcDateTime,
                NextAttemptAt = draft.NextAttemptAt?.UtcDateTime,
                UpdatedAt = now.UtcDateTime,
                PortId = draft.PortId,
            };

            context.OutboxMessages.Add(record);
            await context.SaveChangesAsync(token).ConfigureAwait(false);
            return (ToEnqueueResult(record, isNew: true), record);
        }, cancellationToken).ConfigureAwait(false);

        if (outcome.Record is not null)
        {
            NotifyCommitted(ToSnapshot(outcome.Record));
        }

        return outcome.Result;
    }

    public Task<OutboxTransitionResult> RecordAttemptAsync(
        Guid id,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default)
        => TransitionAsync("outbox.record-attempt", id, (record, now) =>
        {
            // The durable record stays Pending for the whole attempt: Sending is an
            // in-memory hint that the wire is in use, never a persisted state.
            if (!MessageDelivery.IsOpen(record.Status))
            {
                return false;
            }

            record.AttemptCount++;
            record.NextAttemptAt = nextAttemptAt.UtcDateTime;
            record.UpdatedAt = now.UtcDateTime;
            return true;
        }, cancellationToken);

    public Task<OutboxTransitionResult> TryReserveNumberAsync(
        Guid id,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
        => writer.EnqueueAsync("outbox.reserve-number", async (context, token) =>
        {
            OutboxMessageRecord? record = await context.OutboxMessages
                .FirstOrDefaultAsync(message => message.Id == id, token).ConfigureAwait(false);
            if (record is null)
            {
                return new OutboxTransitionResult(OutboxTransitionOutcome.UnknownMessage, null);
            }

            if (!MessageDelivery.IsOpen(record.Status))
            {
                return new OutboxTransitionResult(OutboxTransitionOutcome.AlreadyTerminal, ToSnapshot(record));
            }

            if (!record.MessageNumber.HasValue)
            {
                record.MessageNumber = await ReserveNumberAsync(context, asOf, token).ConfigureAwait(false);
                if (record.MessageNumber.HasValue)
                {
                    record.Status = MessageDelivery.Fire(record.Status, DeliveryTrigger.ReserveWireNumber);
                    record.NextAttemptAt = nextAttemptAt.UtcDateTime;
                    record.UpdatedAt = asOf.UtcDateTime;
                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                    NotifyCommitted(ToSnapshot(record));
                }
            }

            return new OutboxTransitionResult(OutboxTransitionOutcome.Applied, ToSnapshot(record));
        }, cancellationToken);

    public Task<OutboxTransitionResult> ApplyDeliveryOutcomeAsync(
        AprsAddress addressee,
        int messageNumber,
        MessageDeliveryStatus outcome,
        CancellationToken cancellationToken = default)
    {
        if (!MessageDelivery.IsTerminal(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A delivery outcome must be terminal.");
        }

        DeliveryTrigger trigger = MessageDelivery.TriggerForOutcome(outcome);

        string addresseeBase = addressee.Base;
        AprsSsid addresseeSsid = ToStoredSsid(addressee);
        return writer.EnqueueAsync("outbox.apply-outcome", async (context, token) =>
        {
            List<OutboxMessageRecord> matches = await context.OutboxMessages
                .Where(message => message.MessageNumber == messageNumber
                    && message.AddresseeBase == addresseeBase
                    && message.AddresseeSsid == addresseeSsid)
                .ToListAsync(token).ConfigureAwait(false);

            if (matches.Count == 0)
            {
                return new OutboxTransitionResult(OutboxTransitionOutcome.UnknownMessage, null);
            }

            OutboxMessageRecord? open = matches.FirstOrDefault(message => MessageDelivery.IsOpen(message.Status));
            if (open is null)
            {
                return new OutboxTransitionResult(
                    OutboxTransitionOutcome.AlreadyTerminal,
                    ToSnapshot(matches[0]));
            }

            Complete(open, trigger, timeProvider.GetUtcNow());
            await context.SaveChangesAsync(token).ConfigureAwait(false);
            NotifyCommitted(ToSnapshot(open));
            return new OutboxTransitionResult(OutboxTransitionOutcome.Applied, ToSnapshot(open));
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxMessageSnapshot>> ExpireAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = asOf.UtcDateTime;
        List<OutboxMessageRecord> expired = await writer.EnqueueAsync(
            "outbox.expire", async (context, token) =>
        {
            List<OutboxMessageRecord> records = await context.OutboxMessages
                .Where(message => (message.Status == MessageDeliveryStatus.Pending
                        || message.Status == MessageDeliveryStatus.Queued)
                    && message.ExpiresAt <= nowUtc)
                .ToListAsync(token).ConfigureAwait(false);

            foreach (OutboxMessageRecord record in records)
            {
                Complete(record, DeliveryTrigger.Expire, asOf);
            }

            if (records.Count > 0)
            {
                await context.SaveChangesAsync(token).ConfigureAwait(false);
            }

            return records;
        }, cancellationToken).ConfigureAwait(false);

        List<OutboxMessageSnapshot> snapshots = expired.Select(ToSnapshot).ToList();
        foreach (OutboxMessageSnapshot snapshot in snapshots)
        {
            NotifyCommitted(snapshot);
        }

        return snapshots;
    }

    public Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        return writer.EnqueueAsync("outbox.cleanup", (context, token) =>
            context.OutboxMessages
                .Where(message => message.Status != MessageDeliveryStatus.Pending
                    && message.Status != MessageDeliveryStatus.Queued
                    && message.NumberReleaseAt != null
                    && message.NumberReleaseAt <= nowUtc)
                .ExecuteDeleteAsync(token), cancellationToken);
    }

    public Task<IReadOnlyList<OutboxMessageSnapshot>> GetOpenAsync(CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        return reader.QueryAsync(async (context, token) =>
        {
            List<OutboxMessageRecord> records = await context.OutboxMessages
                .Where(message => (message.Status == MessageDeliveryStatus.Pending
                        || message.Status == MessageDeliveryStatus.Queued)
                    && message.ExpiresAt > nowUtc)
                .OrderByDescending(message => message.CreatedAt)
                .ToListAsync(token).ConfigureAwait(false);
            return (IReadOnlyList<OutboxMessageSnapshot>)records.Select(ToSnapshot).ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<OutboxMessageSnapshot>> GetDueAsync(CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        return reader.QueryAsync(async (context, token) =>
        {
            List<OutboxMessageRecord> records = await context.OutboxMessages
                .Where(message => (message.Status == MessageDeliveryStatus.Pending
                        || message.Status == MessageDeliveryStatus.Queued)
                    && message.ExpiresAt > nowUtc
                    && message.NextAttemptAt != null
                    && message.NextAttemptAt <= nowUtc)
                .OrderBy(message => message.CreatedAt)
                .ToListAsync(token).ConfigureAwait(false);
            return (IReadOnlyList<OutboxMessageSnapshot>)records.Select(ToSnapshot).ToList();
        }, cancellationToken);
    }

    public Task<OutboxMessageSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => reader.QueryAsync(async (context, token) =>
        {
            OutboxMessageRecord? record = await context.OutboxMessages
                .FirstOrDefaultAsync(message => message.Id == id, token).ConfigureAwait(false);
            return record is null ? null : ToSnapshot(record);
        }, cancellationToken);

    public Task<IReadOnlyList<OutboxMessageSnapshot>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<OutboxMessageSnapshot>>([]);
        }

        return reader.QueryAsync(async (context, token) =>
        {
            List<OutboxMessageRecord> records = await context.OutboxMessages
                .Where(message => ids.Contains(message.Id))
                .ToListAsync(token).ConfigureAwait(false);
            return (IReadOnlyList<OutboxMessageSnapshot>)records.Select(ToSnapshot).ToList();
        }, cancellationToken);
    }

    private Task<OutboxTransitionResult> TransitionAsync(
        string operation,
        Guid id,
        Func<OutboxMessageRecord, DateTimeOffset, bool> apply,
        CancellationToken cancellationToken)
        => writer.EnqueueAsync(operation, async (context, token) =>
        {
            OutboxMessageRecord? record = await context.OutboxMessages
                .FirstOrDefaultAsync(message => message.Id == id, token).ConfigureAwait(false);
            if (record is null)
            {
                return new OutboxTransitionResult(OutboxTransitionOutcome.UnknownMessage, null);
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            if (!apply(record, now))
            {
                return new OutboxTransitionResult(OutboxTransitionOutcome.AlreadyTerminal, ToSnapshot(record));
            }

            await context.SaveChangesAsync(token).ConfigureAwait(false);
            NotifyCommitted(ToSnapshot(record));
            return new OutboxTransitionResult(OutboxTransitionOutcome.Applied, ToSnapshot(record));
        }, cancellationToken);

    private async Task<int?> ReserveNumberAsync(
        AppDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = now.UtcDateTime;
        List<int> held = await context.OutboxMessages
            .Where(message => message.MessageNumber != null
                && (message.Status == MessageDeliveryStatus.Pending
                    || (message.NumberReleaseAt != null && message.NumberReleaseAt > nowUtc)))
            .Select(message => message.MessageNumber!.Value)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        HashSet<int> taken = [.. held];
        for (int candidate = 1; candidate <= maximumWireNumber; candidate++)
        {
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Records a terminal outcome on the durable record.</summary>
    /// <param name="record">The record being resolved.</param>
    /// <param name="trigger">The outcome that was observed.</param>
    /// <param name="now">The instant the outcome was applied.</param>
    private static void Complete(OutboxMessageRecord record, DeliveryTrigger trigger, DateTimeOffset now)
    {
        record.Status = MessageDelivery.Fire(record.Status, trigger);
        record.NextAttemptAt = null;
        record.NumberReleaseAt = now.Add(NumberReuseGuard).UtcDateTime;
        record.UpdatedAt = now.UtcDateTime;
    }

    /// <summary>Maps a recorded outcome onto the trigger that produces it.</summary>
    private static DeliveryTrigger TriggerFor(MessageDeliveryStatus outcome) =>
        MessageDelivery.TriggerForOutcome(outcome);

    private static AprsSsid ToStoredSsid(AprsAddress address) => (AprsSsid)(address.Ssid ?? 0);

    private static OutboxMessageSnapshot ToSnapshot(OutboxMessageRecord record) => new(
        record.Id,
        AprsAddress.FromStored(record.AddresseeBase, (int)record.AddresseeSsid),
        record.Text,
        record.MessageNumber,
        record.Status,
        record.AttemptCount,
        new DateTimeOffset(record.CreatedAt, TimeSpan.Zero),
        record.NextAttemptAt.HasValue ? new DateTimeOffset(record.NextAttemptAt.Value, TimeSpan.Zero) : null,
        new DateTimeOffset(record.ExpiresAt, TimeSpan.Zero),
        record.PortId);

    private static OutboxEnqueueResult ToEnqueueResult(OutboxMessageRecord record, bool isNew) => new(
        record.Id,
        record.MessageNumber,
        record.Status,
        isNew,
        record.AttemptCount,
        record.NextAttemptAt.HasValue ? new DateTimeOffset(record.NextAttemptAt.Value, TimeSpan.Zero) : null);

    private void NotifyCommitted(OutboxMessageSnapshot snapshot)
    {
        if (MessageCommitted is not { } handlers)
        {
            return;
        }

        foreach (EventHandler<OutboxMessageChangedEventArgs> handler in
            handlers.GetInvocationList().Cast<EventHandler<OutboxMessageChangedEventArgs>>())
        {
            try
            {
                handler(this, new OutboxMessageChangedEventArgs(snapshot));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox subscriber failed after commit.");
            }
        }
    }
}

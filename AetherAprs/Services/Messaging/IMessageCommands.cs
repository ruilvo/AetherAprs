// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Messaging;

/// <summary>
/// Durable outbound message commands: enqueue, attempt updates, ACK/REJ
/// transitions, expiry and cleanup. The implementation serializes every write
/// and raises <see cref="MessageCommitted"/> only after the commit.
/// </summary>
public interface IMessageCommands
{
    /// <summary>Raised once per committed change, after the transaction.</summary>
    event EventHandler<OutboxMessageChangedEventArgs>? MessageCommitted;

    /// <summary>
    /// Inserts the logical message if it is new, otherwise returns the existing
    /// record. The wire number is reserved in the same transaction when one is
    /// free; a message without a number is queued instead of reusing an active one.
    /// </summary>
    Task<OutboxEnqueueResult> EnqueueAsync(OutboxMessageDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Records one transmission attempt against an open message.</summary>
    Task<OutboxTransitionResult> RecordAttemptAsync(
        Guid id,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default);

    /// <summary>Reserves a wire number for a queued message once one is free.</summary>
    /// <param name="id">The logical message identity.</param>
    /// <param name="nextAttemptAt">When the next transmission attempt is due.</param>
    /// <param name="asOf">
    /// The instant the decision is made for. The caller captures it when it decides
    /// to act; reading the clock inside the command would let a queued write observe
    /// a later instant than the pass that scheduled it.
    /// </param>
    /// <param name="cancellationToken">Abandons the write.</param>
    Task<OutboxTransitionResult> TryReserveNumberAsync(
        Guid id,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an ACK, REJ or terminal status to the open message that matches the
    /// normalized peer identity and wire number. Terminal state never regresses.
    /// </summary>
    Task<OutboxTransitionResult> ApplyDeliveryOutcomeAsync(
        AprsAddress addressee,
        int messageNumber,
        MessageDeliveryStatus outcome,
        CancellationToken cancellationToken = default);

    /// <summary>Marks open messages past their expiry as expired and frees their numbers.</summary>
    /// <param name="asOf">
    /// The instant the decision is made for. See <see cref="TryReserveNumberAsync"/>.
    /// </param>
    /// <param name="cancellationToken">Abandons the write.</param>
    Task<IReadOnlyList<OutboxMessageSnapshot>> ExpireAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes terminal messages whose number reuse guard has elapsed.</summary>
    Task<int> CleanupAsync(CancellationToken cancellationToken = default);
}

/// <summary>Read-side access to the durable outbound message records.</summary>
public interface IMessageQueries
{
    /// <summary>Open messages that have not expired, newest first.</summary>
    Task<IReadOnlyList<OutboxMessageSnapshot>> GetOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Open messages whose next attempt is due.</summary>
    Task<IReadOnlyList<OutboxMessageSnapshot>> GetDueAsync(CancellationToken cancellationToken = default);

    /// <summary>Looks up one message by its logical identity.</summary>
    Task<OutboxMessageSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Looks up several messages by logical identity, skipping unknown ids.</summary>
    Task<IReadOnlyList<OutboxMessageSnapshot>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);
}

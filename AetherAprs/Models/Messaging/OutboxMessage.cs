// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using System;

namespace AetherAprs.Models.Messaging;

/// <summary>
/// A logical outbound message offered to the outbox. The caller owns
/// <see cref="Id"/> so a retried enqueue resolves to the same durable record.
/// </summary>
public sealed record OutboxMessageDraft(
    Guid Id,
    AprsAddress Addressee,
    string Text,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    Guid? PortId,
    DateTimeOffset? NextAttemptAt = null);

/// <summary>Immutable view of one durable outbound message.</summary>
public sealed record OutboxMessageSnapshot(
    Guid Id,
    AprsAddress Addressee,
    string Text,
    int? MessageNumber,
    MessageDeliveryStatus Status,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset ExpiresAt,
    Guid? PortId);

/// <summary>Result of offering a logical message to the outbox.</summary>
public sealed record OutboxEnqueueResult(
    Guid Id,
    int? MessageNumber,
    MessageDeliveryStatus Status,
    bool IsNew,
    int AttemptCount,
    DateTimeOffset? NextAttemptAt);

/// <summary>Why a delivery transition was or was not applied.</summary>
public enum OutboxTransitionOutcome
{
    /// <summary>The transition was written.</summary>
    Applied,

    /// <summary>No outbound message matches the identity and number.</summary>
    UnknownMessage,

    /// <summary>The message already reached a terminal state.</summary>
    AlreadyTerminal,
}

/// <summary>Result of a delivery transition, with the durable state after the commit.</summary>
public sealed record OutboxTransitionResult(
    OutboxTransitionOutcome Outcome,
    OutboxMessageSnapshot? Message);

/// <summary>Raised after an outbox command committed.</summary>
public sealed class OutboxMessageChangedEventArgs(OutboxMessageSnapshot message) : EventArgs
{
    public OutboxMessageSnapshot Message { get; } = message;
}

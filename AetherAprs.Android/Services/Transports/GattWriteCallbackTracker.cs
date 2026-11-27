// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;

namespace AetherAprs.Android.Services.Transports;

/// <summary>
/// What one <c>OnCharacteristicWrite</c> callback means for the stream.
/// </summary>
/// <param name="Token">The write the callback belongs to, or null for a stale callback.</param>
/// <param name="IsAbandoned">Whether the awaiting write had already been abandoned.</param>
internal readonly record struct GattWriteCallback(object? Token, bool IsAbandoned)
{
    /// <summary>
    /// Whether the callback belongs to a write that is still awaiting its
    /// acknowledgement. A stale callback must not complete or release anything.
    /// </summary>
    public bool IsStale => Token is null;

    /// <summary>
    /// Whether the callback must release the write gate. A write-with-response whose
    /// await was abandoned keeps the gate held until its late callback arrives, so
    /// no two ATT writes are ever in flight at once.
    /// </summary>
    public bool ReleasesGate => IsAbandoned;
}

/// <summary>
/// Tracks the single in-flight write-with-response and decides what an arriving
/// acknowledgement means. Kept free of Android types so the stale-callback rules can
/// be unit-tested.
/// </summary>
/// <remarks>
/// Android delivers <c>OnCharacteristicWrite</c> on the GATT thread with no
/// correlation identifier. The only safe protocol is to allow exactly one write in
/// flight: a cancelled write keeps the gate held until its acknowledgement is
/// consumed, so a late callback can never be mistaken for the next write's
/// acknowledgement.
/// </remarks>
internal sealed class GattWriteCallbackTracker
{
    private readonly Lock _sync = new();
    private object? _pending;
    private bool _abandoned;
    private int _sequence;

    /// <summary>Whether a write is awaiting its acknowledgement.</summary>
    public bool HasPendingWrite
    {
        get
        {
            lock (_sync)
            {
                return _pending is not null;
            }
        }
    }

    /// <summary>
    /// Registers a new in-flight write and returns the token its callback must
    /// match.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A previous write is still awaiting its acknowledgement, which means the
    /// caller did not hold the write gate.
    /// </exception>
    public object Register()
    {
        lock (_sync)
        {
            if (_pending is not null)
            {
                throw new InvalidOperationException(
                    "A BLE characteristic write is already awaiting its acknowledgement.");
            }

            object token = ++_sequence;
            _pending = token;
            _abandoned = false;
            return token;
        }
    }

    /// <summary>
    /// Drops a registration whose write never reached the stack, so the gate can be
    /// released by the caller without waiting for a callback that will not arrive.
    /// </summary>
    /// <returns><see langword="true"/> when the token was the pending write.</returns>
    public bool TryCancel(object token)
    {
        ArgumentNullException.ThrowIfNull(token);

        lock (_sync)
        {
            if (!ReferenceEquals(_pending, token))
            {
                return false;
            }

            _pending = null;
            _abandoned = false;
            return true;
        }
    }

    /// <summary>
    /// Marks the pending write as abandoned (its await was cancelled). The write gate
    /// stays held until the acknowledgement arrives.
    /// </summary>
    /// <param name="token">Token of the write that was abandoned.</param>
    /// <returns><see langword="true"/> when this call abandoned the pending write.</returns>
    public bool Abandon(object token)
    {
        ArgumentNullException.ThrowIfNull(token);

        lock (_sync)
        {
            if (!ReferenceEquals(_pending, token))
            {
                return false;
            }

            _abandoned = true;
            return true;
        }
    }

    /// <summary>
    /// Consumes an acknowledgement. A callback that arrives with no pending write is
    /// stale and releases nothing.
    /// </summary>
    public GattWriteCallback Complete()
    {
        lock (_sync)
        {
            object? token = _pending;
            _pending = null;

            bool abandoned = _abandoned;
            _abandoned = false;

            return new GattWriteCallback(token, abandoned);
        }
    }

    /// <summary>
    /// Consumes an acknowledgement for a write that was registered but never
    /// abandoned, and reports whether the gate was released by an earlier abandoned
    /// write. Exposed so a disposed stream can be reset without leaking the gate.
    /// </summary>
    public bool Clear()
    {
        lock (_sync)
        {
            bool abandoned = _abandoned;
            _pending = null;
            _abandoned = false;
            return abandoned;
        }
    }
}

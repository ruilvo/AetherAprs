// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;

namespace AetherAprs.Services.Ports;

/// <summary>
/// One forwarding outcome for the route/reason history. The record deliberately holds
/// only routing facts and a reason; packet text, positions and frames never enter it.
/// </summary>
/// <param name="RecordedAt">When the decision was made, in UTC.</param>
/// <param name="SourcePortId">The port that received the candidate.</param>
/// <param name="TargetPortId">The port the decision applies to, or <c>null</c> for a packet-level decision.</param>
/// <param name="Reason">The accept or reject reason.</param>
public readonly record struct DigipeatDecisionRecord(
    DateTimeOffset RecordedAt,
    Guid SourcePortId,
    Guid? TargetPortId,
    DigipeatReason Reason);

/// <summary>
/// Bounded forwarding counters and a bounded route/reason history. Shared by the
/// policy service and the router so one place answers "what happened to this route?"
/// without retaining payloads and without unbounded growth.
/// </summary>
public sealed class DigipeatDecisionLog
{
    /// <summary>Default number of recent decisions retained.</summary>
    public const int DefaultCapacity = 256;

    private readonly Lock _lock = new();
    private readonly DigipeatDecisionRecord[] _history;
    private readonly long[] _counts;
    private int _next;
    private int _count;

    /// <summary>Initializes a new instance of <see cref="DigipeatDecisionLog"/>.</summary>
    /// <param name="capacity">Number of recent decisions to retain; must be positive.</param>
    public DigipeatDecisionLog(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1, nameof(capacity));
        _history = new DigipeatDecisionRecord[capacity];
        _counts = new long[Enum.GetValues<DigipeatReason>().Length];
    }

    /// <summary>Gets the maximum number of decisions retained in the history.</summary>
    public int Capacity => _history.Length;

    /// <summary>Records one decision. Older entries are overwritten once capacity is reached.</summary>
    /// <param name="recordedAt">When the decision was made.</param>
    /// <param name="sourcePortId">The port that received the candidate.</param>
    /// <param name="targetPortId">The target port, or <c>null</c> for a packet-level decision.</param>
    /// <param name="reason">The accept or reject reason.</param>
    public void Record(DateTimeOffset recordedAt, Guid sourcePortId, Guid? targetPortId, DigipeatReason reason)
    {
        lock (_lock)
        {
            _history[_next] = new DigipeatDecisionRecord(recordedAt, sourcePortId, targetPortId, reason);
            _next = (_next + 1) % _history.Length;
            _count = Math.Min(_count + 1, _history.Length);
            _counts[(int)reason]++;
        }
    }

    /// <summary>
    /// Returns the most recent decisions, oldest first. The result is a snapshot that
    /// the caller may enumerate without holding the lock.
    /// </summary>
    /// <param name="maximum">Maximum number of decisions to return; defaults to the whole history.</param>
    public IReadOnlyList<DigipeatDecisionRecord> GetRecentDecisions(int maximum = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 0, nameof(maximum));

        lock (_lock)
        {
            int take = Math.Min(maximum, _count);
            if (take == 0)
            {
                return [];
            }

            DigipeatDecisionRecord[] result = new DigipeatDecisionRecord[take];
            int start = (_next - take + _history.Length) % _history.Length;
            for (int index = 0; index < take; index++)
            {
                result[index] = _history[(start + index) % _history.Length];
            }

            return result;
        }
    }

    /// <summary>Gets the total number of decisions recorded with the given reason.</summary>
    public long GetCount(DigipeatReason reason)
    {
        lock (_lock)
        {
            return _counts[(int)reason];
        }
    }

    /// <summary>Gets a snapshot of every counter, keyed by reason.</summary>
    public IReadOnlyDictionary<DigipeatReason, long> GetCounts()
    {
        lock (_lock)
        {
            Dictionary<DigipeatReason, long> counts = [];
            foreach (DigipeatReason reason in Enum.GetValues<DigipeatReason>())
            {
                counts[reason] = _counts[(int)reason];
            }

            return counts;
        }
    }
}

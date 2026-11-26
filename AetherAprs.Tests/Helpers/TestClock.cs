// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Tests.Helpers;

/// <summary>
/// A deterministic <see cref="TimeProvider"/>: the wall clock never moves on its
/// own and timers only fire when a test calls <see cref="Advance"/>. Retry,
/// lease, retention and expiry behaviour can therefore be asserted against
/// virtual time instead of real delays.
/// </summary>
public sealed class TestClock : TimeProvider, IDisposable
{
    /// <summary>
    /// A fixed, arbitrary starting instant for tests that do not care about the
    /// calendar date.
    /// </summary>
    public static readonly DateTimeOffset DefaultStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Guards the current instant and the scheduled timers.</summary>
    private readonly Lock _sync = new();

    private readonly List<VirtualTimer> _timers = [];
    private readonly SemaphoreSlim _timerCreated = new(0);
    private DateTimeOffset _utcNow;

    /// <summary>
    /// Initializes a clock at the given instant, or at <see cref="DefaultStart"/>.
    /// </summary>
    /// <param name="start">The instant the clock starts at.</param>
    public TestClock(DateTimeOffset? start = null) => _utcNow = start ?? DefaultStart;

    /// <summary>The current instant, for assertions that do not want to call the API.</summary>
    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_sync)
            {
                return _utcNow;
            }
        }
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => UtcNow;

    /// <summary>The current instant as a UTC <see cref="DateTime"/>.</summary>
    public DateTime UtcDateTime => UtcNow.UtcDateTime;

    /// <inheritdoc />
    public override long GetTimestamp() => UtcNow.UtcTicks;

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>How many timers are currently scheduled and not yet fired or disposed.</summary>
    public int PendingTimerCount
    {
        get
        {
            lock (_sync)
            {
                int pending = 0;
                foreach (VirtualTimer timer in _timers)
                {
                    if (timer.Enabled)
                    {
                        pending++;
                    }
                }

                return pending;
            }
        }
    }

    /// <summary>
    /// Waits until the subject schedules a timer, which proves it is now waiting
    /// for virtual time instead of racing the test.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the next timer is created.</returns>
    public Task WaitForTimerAsync(CancellationToken cancellationToken = default) =>
        _timerCreated.WaitAsync(cancellationToken);

    /// <summary>
    /// Moves the clock forward and fires every timer that becomes due, in due order.
    /// </summary>
    /// <param name="delta">How far to move forward; it must not be negative.</param>
    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);

        DateTimeOffset target;
        lock (_sync)
        {
            target = _utcNow + delta;
        }

        // A re-armed timer may be due before the target; keep firing until the
        // clock reaches the target. The bound turns a self-rearming zero-delay
        // timer into a clear failure instead of a hang.
        int fired = 0;
        while (true)
        {
            VirtualTimer? due = null;
            lock (_sync)
            {
                DateTimeOffset earliest = DateTimeOffset.MaxValue;
                foreach (VirtualTimer timer in _timers)
                {
                    if (!timer.Enabled || timer.DueAt > target || timer.DueAt >= earliest)
                    {
                        continue;
                    }

                    earliest = timer.DueAt;
                    due = timer;
                }

                if (due is null)
                {
                    _utcNow = target;
                    return;
                }

                _utcNow = due.DueAt;
                due.RearmAfterFiring();
            }

            if (++fired > 10_000)
            {
                throw new InvalidOperationException("A timer kept rescheduling itself without advancing virtual time.");
            }

            // Outside the lock: the callback may schedule another timer or await
            // a task whose continuation schedules one.
            due.Callback(due.State);
        }
    }

    /// <summary>Releases the wait handle used by <see cref="WaitForTimerAsync"/>.</summary>
    public void Dispose() => _timerCreated.Dispose();

    /// <inheritdoc />
    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        VirtualTimer timer = new(this, callback, state);
        lock (_sync)
        {
            _timers.Add(timer);
            timer.Schedule(dueTime, period, _utcNow);
        }

        _timerCreated.Release();
        return timer;
    }

    private void Remove(VirtualTimer timer)
    {
        lock (_sync)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class VirtualTimer(TestClock owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period;
        private bool _enabled;

        public DateTimeOffset DueAt { get; private set; }

        public TimerCallback Callback { get; } = callback;

        public object? State { get; } = state;

        public bool Enabled
        {
            get
            {
                lock (owner._sync)
                {
                    return _enabled;
                }
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._sync)
            {
                Schedule(dueTime, period, owner._utcNow);
            }

            return true;
        }

        /// <summary>Schedules the next due instant. Callers hold the clock lock.</summary>
        public void Schedule(TimeSpan dueTime, TimeSpan period, DateTimeOffset now)
        {
            _period = period;
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                _enabled = false;
                return;
            }

            DueAt = now + dueTime;
            _enabled = true;
        }

        /// <summary>Advances a fired timer to its next occurrence. Callers hold the clock lock.</summary>
        public void RearmAfterFiring()
        {
            if (_period > TimeSpan.Zero)
            {
                DueAt += _period;
                return;
            }

            _enabled = false;
        }

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

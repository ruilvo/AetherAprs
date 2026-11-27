// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Fixed-window forwarding rate limiter keyed by source port. The key set is bounded
/// to the number of ports a runtime can realistically present; when it is exceeded the
/// oldest window is evicted, so a stream of unknown port ids cannot grow memory.
/// </summary>
internal sealed class DigipeatRateLimiter
{
    private const int MaximumTrackedPorts = 64;

    private readonly Lock _lock = new();
    private readonly DigipeatRateLimitOptions _options;
    private readonly Dictionary<Guid, Window> _windows = [];

    public DigipeatRateLimiter(DigipeatRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxForwardsPerWindow, 1, nameof(options.MaxForwardsPerWindow));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Window, TimeSpan.Zero, nameof(options.Window));
        _options = options;
    }

    /// <summary>
    /// Counts one forward for the source port and reports whether it fits the window.
    /// </summary>
    public bool TryConsume(Guid sourcePortId, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_windows.TryGetValue(sourcePortId, out Window window))
            {
                if (_windows.Count >= MaximumTrackedPorts)
                {
                    EvictOldest();
                }

                _windows[sourcePortId] = new Window(now, 1);
                return true;
            }

            if (now - window.StartedAt >= _options.Window)
            {
                _windows[sourcePortId] = new Window(now, 1);
                return true;
            }

            if (window.Count >= _options.MaxForwardsPerWindow)
            {
                return false;
            }

            _windows[sourcePortId] = window with { Count = window.Count + 1 };
            return true;
        }
    }

    private void EvictOldest()
    {
        Guid oldestId = default;
        DateTimeOffset oldestStart = DateTimeOffset.MaxValue;

        foreach ((Guid id, Window window) in _windows)
        {
            if (window.StartedAt < oldestStart)
            {
                oldestStart = window.StartedAt;
                oldestId = id;
            }
        }

        _windows.Remove(oldestId);
    }

    private readonly record struct Window(DateTimeOffset StartedAt, int Count);
}

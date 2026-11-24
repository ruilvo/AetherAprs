// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading;

namespace AetherAprs.Services.Platform;

/// <summary>
/// A one-way latch that is set at most once for the lifetime of the process.
/// </summary>
/// <remarks>
/// Use this for "already asked" decisions that must survive Android activity
/// re-creation (for example, a configuration change the activity does not handle)
/// but must not survive a process restart. A fresh process always starts with the
/// latch clear, so the user is asked again.
/// </remarks>
public sealed class ProcessLifetimeFlag
{
    private int _isSet;

    /// <summary>
    /// Gets a value indicating whether the flag has already been set.
    /// </summary>
    public bool IsSet => Volatile.Read(ref _isSet) != 0;

    /// <summary>
    /// Sets the flag and reports whether this caller was the first to do so.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> for the first caller; <see langword="false"/> for every
    /// later caller, including concurrent ones.
    /// </returns>
    public bool TrySet() => Interlocked.CompareExchange(ref _isSet, 1, 0) == 0;
}

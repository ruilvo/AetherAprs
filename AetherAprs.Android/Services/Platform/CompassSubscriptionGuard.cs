// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Tracks whether the compass sensor listeners are registered so re-entrant
/// start/stop/dispose calls cannot register twice or unregister twice. Kept free
/// of Android types so the idempotency rules can be unit-tested.
/// </summary>
internal sealed class CompassSubscriptionGuard
{
    private readonly Lock _sync = new();
    private bool _subscribed;
    private bool _disposed;

    /// <summary>Gets whether the listeners are currently registered.</summary>
    public bool IsSubscribed
    {
        get
        {
            lock (_sync)
            {
                return _subscribed;
            }
        }
    }

    /// <summary>Gets whether the owner has been disposed.</summary>
    public bool IsDisposed
    {
        get
        {
            lock (_sync)
            {
                return _disposed;
            }
        }
    }

    /// <summary>
    /// Claims the subscription. Returns true only for the call that must register
    /// the listeners; a re-entry, or a call after disposal, returns false.
    /// </summary>
    public bool TryStart()
    {
        lock (_sync)
        {
            if (_disposed || _subscribed)
            {
                return false;
            }

            _subscribed = true;
            return true;
        }
    }

    /// <summary>
    /// Releases the subscription. Returns true only for the call that must
    /// unregister the listeners; a second stop returns false.
    /// </summary>
    public bool TryStop()
    {
        lock (_sync)
        {
            if (!_subscribed)
            {
                return false;
            }

            _subscribed = false;
            return true;
        }
    }

    /// <summary>
    /// Marks the owner disposed. Returns true when listeners were still
    /// registered and the caller must unregister them; repeated calls return
    /// false.
    /// </summary>
    public bool DisposeOnce()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            _disposed = true;
            bool wasSubscribed = _subscribed;
            _subscribed = false;
            return wasSubscribed;
        }
    }
}

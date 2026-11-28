// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;

namespace AetherAprs.Services.Platform;

/// <summary>Audio route the AFSK modem is currently attached to.</summary>
public enum AudioRouteKind
{
    /// <summary>The route is not known yet.</summary>
    Unknown,

    /// <summary>The built-in speaker.</summary>
    Speaker,

    /// <summary>A wired headset with a microphone.</summary>
    WiredHeadset,

    /// <summary>Wired headphones without a microphone.</summary>
    WiredHeadphones,

    /// <summary>A Bluetooth audio device.</summary>
    Bluetooth,

    /// <summary>A USB audio device.</summary>
    Usb
}

/// <summary>
/// Point-in-time audio route and focus state. <see cref="Generation"/> changes
/// whenever the platform switches route, so an authorization for an older
/// generation cannot be reused after a switch.
/// </summary>
/// <param name="Route">The current route.</param>
/// <param name="IsFocusGranted">Whether audio focus is currently held.</param>
/// <param name="IsBecomingNoisy">Whether the route is being removed (headset unplugged).</param>
/// <param name="Generation">Monotonic route generation.</param>
public readonly record struct AudioRouteSnapshot(
    AudioRouteKind Route,
    bool IsFocusGranted,
    bool IsBecomingNoisy,
    int Generation);

/// <summary>Why a route/focus snapshot does not permit transmission.</summary>
public enum AudioTransmitBlockReason
{
    /// <summary>The snapshot permits transmission.</summary>
    None,

    /// <summary>Audio focus is not held, so a call could take the route over.</summary>
    FocusLost,

    /// <summary>The route is being removed (for example a headset was unplugged).</summary>
    RouteBecomingNoisy,

    /// <summary>The route was never resolved.</summary>
    RouteUnknown,

    /// <summary>The route changed after the previous authorization.</summary>
    RouteChanged
}

/// <summary>
/// Produces the route and focus snapshots the transmit gate authorizes against.
/// </summary>
/// <remarks>
/// The platform layer forwards raw observations (a device was added or removed,
/// focus was granted or lost, the route is becoming noisy) into this type instead
/// of handing hand-built snapshots to the gate. The monitor owns the generation
/// counter, so an authorization and the mid-frame revalidation cannot disagree
/// about which route they are talking about. It is free of platform types, so the
/// observation sequence can be unit-tested without a device.
/// </remarks>
public sealed class AudioRouteMonitor
{
    private readonly Lock _lock = new();
    private AudioRouteKind _route = AudioRouteKind.Unknown;
    private bool _isFocusGranted;
    private bool _isBecomingNoisy;
    private int _generation;

    /// <summary>Gets the current route and focus snapshot.</summary>
    public AudioRouteSnapshot Current
    {
        get
        {
            lock (_lock)
            {
                return CreateSnapshotLocked();
            }
        }
    }

    /// <summary>
    /// Records the route the platform is using now. A different route advances the
    /// generation and clears the becoming-noisy flag, because the route change is
    /// the outcome of the device that was being removed.
    /// </summary>
    /// <param name="route">The route the platform reports.</param>
    /// <returns>The snapshot after the observation.</returns>
    public AudioRouteSnapshot SetRoute(AudioRouteKind route)
    {
        lock (_lock)
        {
            if (_route != route)
            {
                _route = route;
                _isBecomingNoisy = false;
                _generation++;
            }

            return CreateSnapshotLocked();
        }
    }

    /// <summary>
    /// Records whether the application currently holds audio focus. Focus is
    /// evaluated separately from the generation, so acquiring or releasing focus
    /// does not by itself invalidate a route authorization.
    /// </summary>
    /// <param name="isGranted">Whether audio focus is held.</param>
    /// <returns>The snapshot after the observation.</returns>
    public AudioRouteSnapshot SetFocusGranted(bool isGranted)
    {
        lock (_lock)
        {
            _isFocusGranted = isGranted;
            return CreateSnapshotLocked();
        }
    }

    /// <summary>
    /// Records that the current route is being removed (for example a headset was
    /// unplugged). The flag blocks transmission until the platform reports the
    /// replacement route.
    /// </summary>
    /// <param name="isBecomingNoisy">Whether the route is being torn down.</param>
    /// <returns>The snapshot after the observation.</returns>
    public AudioRouteSnapshot SetBecomingNoisy(bool isBecomingNoisy)
    {
        lock (_lock)
        {
            if (_isBecomingNoisy != isBecomingNoisy)
            {
                _isBecomingNoisy = isBecomingNoisy;
                _generation++;
            }

            return CreateSnapshotLocked();
        }
    }

    /// <summary>
    /// Forgets every observation, for example when platform observation stops.
    /// The generation still advances so an authorization granted before the reset
    /// is never reused afterwards.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _route = AudioRouteKind.Unknown;
            _isFocusGranted = false;
            _isBecomingNoisy = false;
            _generation++;
        }
    }

    private AudioRouteSnapshot CreateSnapshotLocked() =>
        new(_route, _isFocusGranted, _isBecomingNoisy, _generation);
}

/// <summary>
/// Chooses the output route a platform with several attached sinks should be
/// treated as using, and classifies the raw device list the platform reports.
/// </summary>
/// <remarks>
/// The platform adapter maps its device types to <see cref="AudioRouteKind"/> and
/// hands the set to <see cref="SelectSink"/>, which keeps the priority policy free
/// of platform types and testable. A wired headset wins over Bluetooth and USB
/// because plugging a cable is a deliberate act; Bluetooth wins over USB because a
/// Bluetooth link is the cable an operator chose to pair.
/// </remarks>
public static class AudioRouteClassifier
{
    private static readonly AudioRouteKind[] Priority =
    [
        AudioRouteKind.WiredHeadset,
        AudioRouteKind.Bluetooth,
        AudioRouteKind.Usb,
        AudioRouteKind.WiredHeadphones,
        AudioRouteKind.Speaker
    ];

    /// <summary>
    /// Selects the route to treat as current from the sinks the platform reports.
    /// </summary>
    /// <param name="attachedSinks">Routes of every attached output device.</param>
    /// <returns>
    /// The highest-priority attached route, or <see cref="AudioRouteKind.Unknown"/>
    /// when none of the reported sinks is a usable route.
    /// </returns>
    public static AudioRouteKind SelectSink(IEnumerable<AudioRouteKind> attachedSinks)
    {
        ArgumentNullException.ThrowIfNull(attachedSinks);

        HashSet<AudioRouteKind> attached = [.. attachedSinks];
        foreach (AudioRouteKind candidate in Priority)
        {
            if (attached.Contains(candidate))
            {
                return candidate;
            }
        }

        return AudioRouteKind.Unknown;
    }
}

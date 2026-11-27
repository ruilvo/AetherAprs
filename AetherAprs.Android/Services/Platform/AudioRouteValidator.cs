// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Android.Services.Platform;

/// <summary>Audio route the modem is currently attached to.</summary>
internal enum AudioRouteKind
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
internal readonly record struct AudioRouteSnapshot(
    AudioRouteKind Route,
    bool IsFocusGranted,
    bool IsBecomingNoisy,
    int Generation);

/// <summary>Why a route/focus snapshot does not permit transmission.</summary>
internal enum AudioTransmitBlockReason
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
/// Decides whether push-to-talk may key the transmitter for a route/focus
/// snapshot. Kept free of Android types so the invalidation rules can be
/// unit-tested.
/// </summary>
/// <remarks>
/// Authorization is bound to the route generation it was granted for. Re-checking
/// a snapshot from a later generation (a headset was plugged in, Bluetooth took
/// over, the speaker-mic route changed) fails with
/// <see cref="AudioTransmitBlockReason.RouteChanged"/> so the caller re-decides
/// before transmitting on a route it never validated.
/// </remarks>
internal sealed class AudioRouteValidator
{
    private int? _authorizedGeneration;

    /// <summary>Gets whether a snapshot has been authorized and not invalidated.</summary>
    public bool IsAuthorized => _authorizedGeneration.HasValue;

    /// <summary>Decides whether a snapshot permits transmission on its own.</summary>
    /// <param name="snapshot">The current route and focus state.</param>
    public static AudioTransmitBlockReason Evaluate(AudioRouteSnapshot snapshot)
    {
        if (snapshot.IsBecomingNoisy)
        {
            return AudioTransmitBlockReason.RouteBecomingNoisy;
        }

        if (!snapshot.IsFocusGranted)
        {
            return AudioTransmitBlockReason.FocusLost;
        }

        if (snapshot.Route == AudioRouteKind.Unknown)
        {
            return AudioTransmitBlockReason.RouteUnknown;
        }

        return AudioTransmitBlockReason.None;
    }

    /// <summary>
    /// Authorizes transmission for a snapshot, recording its route generation.
    /// Clears any previous authorization when the snapshot is blocked.
    /// </summary>
    /// <param name="snapshot">The route and focus state to authorize.</param>
    public AudioTransmitBlockReason Authorize(AudioRouteSnapshot snapshot)
    {
        AudioTransmitBlockReason reason = Evaluate(snapshot);
        _authorizedGeneration = reason == AudioTransmitBlockReason.None
            ? snapshot.Generation
            : null;

        return reason;
    }

    /// <summary>
    /// Re-checks a snapshot against the generation that was authorized.
    /// </summary>
    /// <param name="snapshot">The route and focus state at transmission time.</param>
    public AudioTransmitBlockReason Revalidate(AudioRouteSnapshot snapshot)
    {
        AudioTransmitBlockReason reason = Evaluate(snapshot);
        if (reason != AudioTransmitBlockReason.None)
        {
            _authorizedGeneration = null;
            return reason;
        }

        if (_authorizedGeneration != snapshot.Generation)
        {
            _authorizedGeneration = null;
            return AudioTransmitBlockReason.RouteChanged;
        }

        return AudioTransmitBlockReason.None;
    }
}

/// <summary>
/// Thrown when playback is refused, or stopped part-way, because the audio route no
/// longer permits transmission.
/// </summary>
internal sealed class AudioRouteBlockedException : InvalidOperationException
{
    /// <summary>Creates the exception for a block reason.</summary>
    /// <param name="message">Diagnostic message.</param>
    /// <param name="blockReason">Why the route does not permit transmission.</param>
    public AudioRouteBlockedException(string message, AudioTransmitBlockReason blockReason)
        : base(message)
    {
        BlockReason = blockReason;
    }

    /// <summary>Why the route does not permit transmission.</summary>
    public AudioTransmitBlockReason BlockReason { get; }
}


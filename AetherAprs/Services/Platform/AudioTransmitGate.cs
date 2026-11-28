// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Decides whether push-to-talk may key the transmitter for a route/focus
/// snapshot. Kept free of platform types so the invalidation rules can be
/// unit-tested.
/// </summary>
/// <remarks>
/// Authorization is bound to the route generation it was granted for. Re-checking
/// a snapshot from a later generation (a headset was plugged in, Bluetooth took
/// over, the speaker-mic route changed) fails with
/// <see cref="AudioTransmitBlockReason.RouteChanged"/> so the caller re-decides
/// before transmitting on a route it never validated.
/// </remarks>
public sealed class AudioRouteValidator
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
/// Decides whether push-to-talk may key, and whether it has to unkey mid-frame.
/// Kept free of platform types so the revalidation rules can be unit-tested.
/// </summary>
/// <remarks>
/// A snapshot is authorized once, when the operator keys. Every transmission after
/// that revalidates a fresh snapshot against the generation that was authorized, so
/// a route switch between keying and the first sample, or a headset unplugged while
/// the frame is going out, aborts the transmission instead of playing modulated
/// audio into the wrong sink.
/// </remarks>
public sealed class AudioTransmitGate
{
    private readonly AudioRouteValidator _validator;

    /// <summary>Creates a gate with its own validator.</summary>
    public AudioTransmitGate()
        : this(new AudioRouteValidator())
    {
    }

    /// <summary>Creates a gate over an existing validator.</summary>
    /// <param name="validator">Validator that owns the authorized generation.</param>
    public AudioTransmitGate(AudioRouteValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        _validator = validator;
    }

    /// <summary>Current transmission state.</summary>
    public AudioTransmitState State { get; private set; } = AudioTransmitState.Idle;

    /// <summary>Why the last call refused transmission, or <see cref="AudioTransmitBlockReason.None"/>.</summary>
    public AudioTransmitBlockReason LastBlockReason { get; private set; } = AudioTransmitBlockReason.None;

    /// <summary>Gets whether the transmitter is keyed and still permitted.</summary>
    public bool IsKeyed => State == AudioTransmitState.Keyed;

    /// <summary>Gets whether a keyed transmission was aborted and needs unkeying.</summary>
    public bool IsAborted => State == AudioTransmitState.Aborted;

    /// <summary>
    /// Keys the transmitter for <paramref name="snapshot"/>. A second call while
    /// keyed revalidates instead of re-authorizing, so releasing and re-keying cannot
    /// launder a route that changed under the operator.
    /// </summary>
    /// <param name="snapshot">Route and focus state when the operator keyed.</param>
    /// <returns>The reason transmission is refused, or None when keyed.</returns>
    public AudioTransmitBlockReason TryKey(AudioRouteSnapshot snapshot)
    {
        if (State == AudioTransmitState.Keyed)
        {
            return CheckBeforeTransmit(snapshot);
        }

        AudioTransmitBlockReason reason = _validator.Authorize(snapshot);
        LastBlockReason = reason;
        State = reason == AudioTransmitBlockReason.None
            ? AudioTransmitState.Keyed
            : AudioTransmitState.Idle;

        return reason;
    }

    /// <summary>
    /// Revalidates immediately before a chunk is handed to the sink. Never grants
    /// authorization: a snapshot from a generation that was not authorized aborts
    /// the transmission instead of being accepted implicitly.
    /// </summary>
    /// <param name="snapshot">Route and focus state at this instant.</param>
    /// <returns>The reason transmission is refused, or None when still permitted.</returns>
    public AudioTransmitBlockReason CheckBeforeTransmit(AudioRouteSnapshot snapshot)
    {
        if (State == AudioTransmitState.Idle)
        {
            // Nothing was keyed, so there is nothing to revalidate.
            LastBlockReason = AudioTransmitBlockReason.None;
            return AudioTransmitBlockReason.None;
        }

        AudioTransmitBlockReason reason = _validator.Revalidate(snapshot);
        LastBlockReason = reason;

        if (reason != AudioTransmitBlockReason.None)
        {
            // The route is gone; the caller must unkey, and the error is terminal for
            // this transmission rather than a transient retry.
            State = AudioTransmitState.Aborted;
        }

        return reason;
    }

    /// <summary>
    /// Records that the transmitter was unkeyed, clearing any abort. Call it after
    /// the platform release, so the next transmission starts from a clean gate.
    /// </summary>
    public void Release()
    {
        State = AudioTransmitState.Idle;
        LastBlockReason = AudioTransmitBlockReason.None;
    }
}

/// <summary>State of one push-to-talk transmission.</summary>
public enum AudioTransmitState
{
    /// <summary>Nothing is keyed.</summary>
    Idle,

    /// <summary>The transmitter is keyed and the route is still valid.</summary>
    Keyed,

    /// <summary>
    /// The transmitter was keyed and the route, focus or device changed, so the
    /// transmission has to stop and the operator has to key again.
    /// </summary>
    Aborted
}

/// <summary>
/// Thrown when playback is refused, or stopped part-way, because the audio route no
/// longer permits transmission.
/// </summary>
public sealed class AudioRouteBlockedException : InvalidOperationException
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

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;
using Android.Media;
using Microsoft.Extensions.Logging;
using System;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Android implementation of <see cref="IPttService"/> that keys the
/// transmitter by toggling the speaker-mic (voice transmit) audio routing via
/// <see cref="AudioManager"/>.
/// </summary>
/// <remarks>
/// Keying goes through the shared <see cref="AudioTransmitGate"/> and the route
/// observed by <see cref="AudioRoutePlatformSource"/>, so a transmission is
/// refused while audio focus is gone, the route is unknown, or a headset is being
/// unplugged. The unconditional <see cref="Engage"/> remains for callers that key
/// a radio without playing modulated audio through this application.
/// </remarks>
public sealed class PttService(
    AudioRoutePlatformSource audioRoute,
    AudioTransmitGate routeGate,
    ILogger<PttService>? logger = null) : IPttService
{
    private readonly AudioManager _audioManager = (AudioManager)global::Android.App.Application.Context
            .GetSystemService(global::Android.Content.Context.AudioService)!;
    private readonly ILogger<PttService>? _logger = logger;
    private readonly AudioRoutePlatformSource _audioRoute = audioRoute;
    private readonly AudioTransmitGate _routeGate = routeGate;
    private bool _isFocusHeld;

    /// <inheritdoc/>
    public bool SupportsPhysicalPtt => true;

    /// <inheritdoc/>
    public void Engage()
    {
        try
        {
#pragma warning disable CA1422 // SpeakerphoneOn is deprecated on API 31+; no direct PTT replacement.
            _audioManager.Mode = Mode.InCommunication;
            _audioManager.SpeakerphoneOn = true;
#pragma warning restore CA1422
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to key PTT (speaker-mic).");
        }
    }

    /// <inheritdoc/>
    public AudioTransmitBlockReason TryEngage()
    {
        if (_routeGate.IsKeyed)
        {
            // Already keyed: revalidate instead of re-authorizing, so a route that
            // changed under the operator cannot be laundered by keying twice.
            return _routeGate.CheckBeforeTransmit(_audioRoute.CurrentSnapshot);
        }

        if (!_audioRoute.TryAcquireAudioFocus())
        {
            return AudioTransmitBlockReason.FocusLost;
        }

        AudioTransmitBlockReason blockReason = _routeGate.TryKey(_audioRoute.CurrentSnapshot);
        if (blockReason != AudioTransmitBlockReason.None)
        {
            _logger?.LogWarning(
                "PTT refused because the audio route does not permit transmission ({BlockReason}).",
                blockReason);
            _audioRoute.ReleaseAudioFocus();
            return blockReason;
        }

        _isFocusHeld = true;
        Engage();

        return AudioTransmitBlockReason.None;
    }

    /// <inheritdoc/>
    public void Release()
    {
        // Unkeying clears the abort so the next transmission starts from a clean gate.
        _routeGate.Release();

        if (_isFocusHeld)
        {
            _isFocusHeld = false;
            _audioRoute.ReleaseAudioFocus();
        }

        try
        {
#pragma warning disable CA1422 // SpeakerphoneOn is deprecated on API 31+; no direct PTT replacement.
            _audioManager.SpeakerphoneOn = false;
            _audioManager.Mode = Mode.Normal;
#pragma warning restore CA1422
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to release PTT (speaker-mic).");
        }
    }
}
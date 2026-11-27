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
public sealed class PttService(ILogger<PttService>? logger = null) : IPttService
{
    private readonly AudioManager _audioManager = (AudioManager)global::Android.App.Application.Context
            .GetSystemService(global::Android.Content.Context.AudioService)!;
    private readonly ILogger<PttService>? _logger = logger;
    private readonly AudioRouteValidator _routeValidator = new();

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

    /// <summary>
    /// Keys the transmitter only when the route/focus snapshot still permits it.
    /// Pass null to keep the unconditional behaviour of <see cref="Engage"/>.
    /// </summary>
    /// <param name="routeSnapshot">
    /// Snapshot captured when the operator requested transmission, or null when
    /// the caller has no route concept.
    /// </param>
    /// <returns>True when the transmitter was keyed.</returns>
    internal bool TryEngage(AudioRouteSnapshot? routeSnapshot)
    {
        if (routeSnapshot is { } snapshot)
        {
            AudioTransmitBlockReason blockReason = _routeValidator.IsAuthorized
                ? _routeValidator.Revalidate(snapshot)
                : _routeValidator.Authorize(snapshot);

            if (blockReason != AudioTransmitBlockReason.None)
            {
                _logger?.LogWarning(
                    "PTT refused because the audio route does not permit transmission ({BlockReason}).",
                    blockReason);
                return false;
            }
        }

        Engage();
        return true;
    }

    /// <inheritdoc/>
    public void Release()
    {
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

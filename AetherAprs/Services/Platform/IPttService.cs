// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// Abstraction over a radio push-to-talk (PTT) control. Implementations key
/// and unkey the transmitter (e.g. Android's speaker-mic toggle via
/// <c>AudioManager</c>).
/// </summary>
public interface IPttService
{
    /// <summary>
    /// Whether the platform exposes a physical PTT control that this application can
    /// key. When <see langword="false"/>, the only honest mode is
    /// <c>PttMode.Vox</c> (the radio keys itself), so editors must not offer
    /// <c>PttMode.SpeakerMic</c>.
    /// </summary>
    bool SupportsPhysicalPtt { get; }

    /// <summary>
    /// Keys the transmitter. Safe to call when already keyed.
    /// </summary>
    /// <remarks>
    /// Callers that transmit modulated audio use <see cref="TryEngage"/> instead, so
    /// the transmitter is not keyed on a route that lost audio focus or is being
    /// unplugged.
    /// </remarks>
    void Engage();

    /// <summary>
    /// Unkeys the transmitter. Safe to call when already unkeyed.
    /// </summary>
    void Release();

    /// <summary>
    /// Requests audio focus and keys the transmitter only when the current audio
    /// route still permits transmission.
    /// </summary>
    /// <returns>
    /// The reason the transmitter was not keyed, or
    /// <see cref="AudioTransmitBlockReason.None"/> when it is keyed.
    /// </returns>
    AudioTransmitBlockReason TryEngage();
}

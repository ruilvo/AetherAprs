// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Abstraction over a captured audio input stream, exposing mono PCM float
/// samples for the AFSK modem. Implementations own the platform audio device
/// (e.g. Android's <c>AudioRecord</c>).
/// </summary>
public interface IAudioStreamService : IDisposable
{
    /// <summary>
    /// Gets the capture sample rate in hertz.
    /// </summary>
    int SampleRate { get; }

    /// <summary>
    /// Raised when a block of mono PCM float samples (range roughly -1..1)
    /// has been captured.
    /// </summary>
    event EventHandler<float[]>? SamplesAvailable;

    /// <summary>
    /// Starts capturing audio. Safe to call when already started.
    /// </summary>
    void StartCapture();

    /// <summary>
    /// Stops capturing audio. Safe to call when already stopped.
    /// </summary>
    void StopCapture();
}

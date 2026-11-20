// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Abstraction over an audio output device that plays mono PCM float samples
/// (range roughly -1..1) for the AFSK modem's transmit path. Implementations
/// own the platform audio sink (e.g. Android's <c>AudioTrack</c>).
/// </summary>
public interface IAudioPlaybackService
{
    /// <summary>
    /// Gets the playback sample rate in hertz.
    /// </summary>
    int SampleRate { get; }

    /// <summary>
    /// Plays a block of mono PCM float samples to completion.
    /// </summary>
    /// <param name="samples">The samples to play.</param>
    /// <param name="cancellationToken">Cancels playback.</param>
    /// <returns>A task that completes when playback has finished.</returns>
    Task PlayAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken = default);
}

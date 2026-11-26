// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Safe default implementation of <see cref="IAudioPlaybackService"/> for
/// platforms without audio output. Playback is a no-op that completes
/// immediately.
/// </summary>
public sealed class NoOpAudioPlaybackService : IAudioPlaybackService
{
    /// <inheritdoc/>
    public int SampleRate => 44100;

    /// <inheritdoc/>
    public Task PlayAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

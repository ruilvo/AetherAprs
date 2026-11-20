// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Safe default implementation of <see cref="IAudioStreamService"/> for
/// platforms without audio capture. Never raises samples.
/// </summary>
public sealed class NoOpAudioStreamService : IAudioStreamService
{
    /// <inheritdoc/>
    public int SampleRate => 44100;

    /// <inheritdoc/>
    public event EventHandler<float[]>? SamplesAvailable
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public void StartCapture()
    {
    }

    /// <inheritdoc/>
    public void StopCapture()
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

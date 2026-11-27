// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;
using Android.Media;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Android implementation of <see cref="IAudioPlaybackService"/> that plays
/// mono PCM float samples through <see cref="AudioTrack"/> as 16-bit PCM.
/// </summary>
public sealed class AudioPlaybackService(ILogger<AudioPlaybackService>? logger = null) : IAudioPlaybackService
{
    private readonly ILogger<AudioPlaybackService>? _logger = logger;
    private readonly AudioRouteValidator _routeValidator = new();

    /// <summary>
    /// Gets or sets the route/focus snapshot captured before transmission. When
    /// set, playback is refused for a blocked route; when null, playback behaves
    /// exactly as before the snapshot existed.
    /// </summary>
    internal AudioRouteSnapshot? RouteSnapshot { get; set; }

    /// <inheritdoc/>
    public int SampleRate => 44100;

    /// <inheritdoc/>
    public async Task PlayAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken = default)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        // Refuse to open the sink for a route that is being torn down or has no
        // audio focus; the caller re-captures the snapshot before each TX.
        if (RouteSnapshot is { } routeSnapshot)
        {
            AudioTransmitBlockReason blockReason = _routeValidator.IsAuthorized
                ? _routeValidator.Revalidate(routeSnapshot)
                : _routeValidator.Authorize(routeSnapshot);
            if (blockReason != AudioTransmitBlockReason.None)
            {
                throw new InvalidOperationException(
                    $"Audio route does not permit playback ({blockReason}).");
            }
        }

        short[] shorts = ToPcm16(samples.Span);
        int bufferSize = Math.Max(
            AudioTrack.GetMinBufferSize(SampleRate, ChannelOut.Mono, Encoding.Pcm16bit),
            shorts.Length * sizeof(short));

        // MODE_STATIC plays the whole buffer exactly once, which matches a
        // burst of modulated AFSK audio.
#pragma warning disable CA1422 // Legacy ctor is the simplest way to request a static, mono PCM16 track.
        using AudioTrack track = new(
            Stream.Music,
            SampleRate,
            ChannelOut.Mono,
            Encoding.Pcm16bit,
            bufferSize,
            AudioTrackMode.Static);
#pragma warning restore CA1422

        if (track.State != AudioTrackState.Initialized)
        {
            throw new InvalidOperationException($"AudioTrack failed to initialize (state {track.State}).");
        }

        AudioTransferResult writeResult = AudioWriteLoop.WriteAll(
            shorts.Length,
            (offset, count) => track.Write(shorts, offset, count),
            cancellationToken);

        if (writeResult.Status == AudioTransferStatus.Aborted)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (!writeResult.IsComplete)
        {
            throw new InvalidOperationException(
                $"AudioTrack accepted {writeResult.Transferred} of {shorts.Length} samples ({writeResult.Status}).");
        }

        track.Play();

        try
        {
            // Await completion: the head advances as the buffer plays, then
            // stops at the frame count once the static buffer is exhausted.
            while (track.PlaybackHeadPosition < shorts.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            track.Stop();
            throw;
        }
        finally
        {
            track.Stop();
        }
    }

    private static short[] ToPcm16(ReadOnlySpan<float> samples)
    {
        short[] shorts = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            float clamped = Math.Clamp(samples[i], -1f, 1f);
            shorts[i] = (short)(clamped * short.MaxValue);
        }

        return shorts;
    }
}

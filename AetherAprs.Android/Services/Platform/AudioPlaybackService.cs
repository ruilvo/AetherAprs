// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Services.Platform;

using Android.Media;

using Microsoft.Extensions.Logging;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Android implementation of <see cref="IAudioPlaybackService"/> that plays
/// mono PCM float samples through <see cref="AudioTrack"/> as 16-bit PCM.
/// </summary>
public sealed class AudioPlaybackService(
    AudioRoutePlatformSource audioRoute,
    AudioTransmitGate routeGate,
    ILogger<AudioPlaybackService>? logger = null) : IAudioPlaybackService
{
    private readonly ILogger<AudioPlaybackService>? _logger = logger;
    private readonly AudioRoutePlatformSource _audioRoute = audioRoute;
    private readonly AudioTransmitGate _routeGate = routeGate;

    /// <inheritdoc/>
    public int SampleRate => 44100;

    /// <inheritdoc/>
    public async Task PlayAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken = default)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        // The PTT path already holds audio focus and authorized this generation
        // when the operator keyed; a VOX transmission owns focus for its burst.
        bool ownsFocus = false;

        try
        {
            if (!_routeGate.IsKeyed)
            {
                if (!_audioRoute.TryAcquireAudioFocus())
                {
                    throw new AudioRouteBlockedException(
                        "Audio route does not permit playback (FocusLost).",
                        AudioTransmitBlockReason.FocusLost);
                }

                ownsFocus = true;
            }

            // Refuse to open the sink for a route that is being torn down or has no
            // audio focus; the snapshot is read from the platform observation rather
            // than a value the caller could have fabricated.
            AudioTransmitBlockReason blockReason = _routeGate.TryKey(_audioRoute.CurrentSnapshot);
            if (blockReason != AudioTransmitBlockReason.None)
            {
                throw new AudioRouteBlockedException(
                    $"Audio route does not permit playback ({blockReason}).",
                    blockReason);
            }

            await PlaySamplesAsync(samples, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Release the authorization before the PTT release, so the next
            // transmission authorizes a fresh generation.
            _routeGate.Release();

            if (ownsFocus)
            {
                _audioRoute.ReleaseAudioFocus();
            }
        }
    }

    private async Task PlaySamplesAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
    {
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
            // A frame can outlast the route it started on, so the sink is revalidated
            // before every chunk against the live platform observation.
            () => _routeGate.CheckBeforeTransmit(_audioRoute.CurrentSnapshot),
            cancellationToken);

        if (writeResult.Status == AudioTransferStatus.Aborted)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (writeResult.IsBlocked)
        {
            // Unkey and drop the frame: playing the remainder would put modulated
            // audio on a route the operator never authorized.
            track.Stop();
            throw new AudioRouteBlockedException(
                $"Audio playback stopped after {writeResult.Transferred} of {shorts.Length} samples ({writeResult.BlockReason}).",
                writeResult.BlockReason);
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

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
/// Android implementation of <see cref="IAudioStreamService"/> that captures
/// mono 16-bit PCM from the microphone via <see cref="AudioRecord"/> and
/// exposes it as float samples in the range roughly -1..1.
/// </summary>
public sealed class AudioStreamService(ILogger<AudioStreamService>? logger = null) : IAudioStreamService
{
    private readonly ILogger<AudioStreamService>? _logger = logger;
    private readonly Lock _lock = new();
    private AudioRecord? _audioRecord;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private bool _disposed;

    /// <inheritdoc/>
    public int SampleRate => 44100;

    /// <inheritdoc/>
    public event EventHandler<float[]>? SamplesAvailable;

    /// <inheritdoc/>
    public void StartCapture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            if (_audioRecord is not null)
            {
                return;
            }

            int minBufferSize = AudioRecord.GetMinBufferSize(
                SampleRate,
                ChannelIn.Mono,
                Encoding.Pcm16bit);

            if (minBufferSize <= 0)
            {
                throw new InvalidOperationException(
                    $"AudioRecord.GetMinBufferSize returned {minBufferSize} — no compatible audio input.");
            }

            // Use a buffer larger than the minimum so the capture thread keeps up
            // without dropping samples on a busy UI thread.
            int bufferSize = Math.Max(minBufferSize, SampleRate / 2);

            AudioRecord audioRecord = new(
                AudioSource.Mic,
                SampleRate,
                ChannelIn.Mono,
                Encoding.Pcm16bit,
                bufferSize);

            if (audioRecord.State != State.Initialized)
            {
                audioRecord.Release();
                throw new InvalidOperationException(
                    $"AudioRecord failed to initialize (state {audioRecord.State}).");
            }

            _audioRecord = audioRecord;
            _captureCts = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoopAsync(audioRecord, bufferSize, _captureCts.Token));
        }

        _logger?.LogInformation("Audio capture started at {SampleRate} Hz.", SampleRate);
    }

    /// <inheritdoc/>
    public void StopCapture()
    {
        AudioRecord? audioRecord;
        CancellationTokenSource? captureCts;
        Task? captureTask;

        lock (_lock)
        {
            audioRecord = _audioRecord;
            captureCts = _captureCts;
            captureTask = _captureTask;
            _audioRecord = null;
            _captureCts = null;
            _captureTask = null;
        }

        if (audioRecord is null)
        {
            return;
        }

        captureCts?.Cancel();

        try
        {
            // StopRecording blocks until the native read loop notices the stop;
            // doing it before joining the task unblocks a pending Read.
            audioRecord.Stop();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error stopping AudioRecord.");
        }

        try
        {
            captureTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation.
        }

        audioRecord.Release();
        captureCts?.Dispose();

        _logger?.LogInformation("Audio capture stopped.");
    }

    private async Task CaptureLoopAsync(AudioRecord audioRecord, int bufferSize, CancellationToken cancellationToken)
    {
        short[] shortBuffer = new short[bufferSize / 2];
        float[] floatBuffer = new float[shortBuffer.Length];
        bool recording = false;

        try
        {
            audioRecord.StartRecording();
            recording = true;

            while (!cancellationToken.IsCancellationRequested)
            {
                // AudioRecord.Read returns 0 when the recording is stopped, and a
                // negative value on a read error; both terminate the loop. Short
                // reads are accumulated so a chunk is not silently truncated.
                AudioTransferResult readResult = await Task.Run(
                    () => AudioWriteLoop.ReadUpTo(
                        shortBuffer.Length,
                        (offset, count) => audioRecord.Read(shortBuffer, offset, count),
                        cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                int shortsRead = readResult.Transferred;
                if (shortsRead <= 0)
                {
                    break;
                }

                for (int i = 0; i < shortsRead; i++)
                {
                    floatBuffer[i] = shortBuffer[i] / 32768f;
                }

                float[] samples = new float[shortsRead];
                Array.Copy(floatBuffer, samples, shortsRead);
                SamplesAvailable?.Invoke(this, samples);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during stop.
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Audio capture loop failed.");
        }
        finally
        {
            if (recording)
            {
                try
                {
                    audioRecord.Stop();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error stopping AudioRecord in capture loop.");
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopCapture();
    }
}

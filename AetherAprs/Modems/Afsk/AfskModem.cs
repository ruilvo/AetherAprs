// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Modems.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Modems.Afsk;

/// <summary>
/// Top-level AFSK modem that decodes received APRS packets from a captured
/// audio stream (Bell 202 AFSK → HDLC → AX.25 → APRS).
///
/// Slice A is receive-only: <see cref="SendAsync"/> throws
/// <see cref="NotSupportedException"/>. Transmission is added in Slice B.
/// </summary>
public sealed class AfskModem : IAprsModem, IAsyncDisposable
{
    private readonly IAudioStreamService _audioStream;
    private readonly AfskDemodulator _demodulator;
    private readonly ILogger<AfskModem> _logger;
    private readonly Lock _lifecycleLock = new();
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="AfskModem"/>.
    /// </summary>
    /// <param name="audioStream">The audio capture source. Must not be null.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="audioStream"/> is null.</exception>
    public AfskModem(IAudioStreamService audioStream, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(audioStream);

        _audioStream = audioStream;
        _demodulator = new AfskDemodulator(audioStream.SampleRate);
        _logger = loggerFactory?.CreateLogger<AfskModem>() ?? NullLogger<AfskModem>.Instance;
    }

    /// <inheritdoc/>
    public event EventHandler<AprsPacket>? PacketReceived;

    /// <inheritdoc/>
    public event EventHandler<Exception>? ReceiveError;

    /// <summary>
    /// Starts audio capture and the demodulation pipeline.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if already started.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lifecycleLock)
        {
            if (_started)
            {
                throw new InvalidOperationException("AfskModem is already started.");
            }

            // Subscribe before starting capture so a synchronous audio source
            // cannot dispatch samples before the handler is attached.
            _audioStream.SamplesAvailable += OnSamplesAvailable;
            _started = true;
        }

        try
        {
            _audioStream.StartCapture();
        }
        catch
        {
            lock (_lifecycleLock)
            {
                _audioStream.SamplesAvailable -= OnSamplesAvailable;
                _started = false;
            }

            throw;
        }

        _logger.LogInformation("AFSK modem started at {SampleRate} Hz.", _audioStream.SampleRate);
    }

    /// <inheritdoc/>
    public Task StopAsync()
    {
        lock (_lifecycleLock)
        {
            if (!_started)
            {
                return Task.CompletedTask;
            }

            _audioStream.SamplesAvailable -= OnSamplesAvailable;
            _started = false;
        }

        _audioStream.StopCapture();
        _logger.LogInformation("AFSK modem stopped.");

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SendAsync(AprsPacket packet, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("AFSK transmission is not supported yet (Slice B).");
    }

    private void OnSamplesAvailable(object? sender, float[] samples)
    {
        try
        {
            var frames = _demodulator.Process(samples);
            foreach (var frame in frames)
            {
                var packet = Ax25Parser.ParseFrame(frame);
                PacketReceived?.Invoke(this, packet);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error decoding AFSK audio block.");
            ReceiveError?.Invoke(this, ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _audioStream.Dispose();
    }
}

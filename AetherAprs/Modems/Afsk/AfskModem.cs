// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
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
/// audio stream (Bell 202 AFSK → HDLC → AX.25 → APRS) and encodes outgoing
/// packets into a played-back audio stream (APRS → AX.25 → HDLC → AFSK), with
/// optional PTT keying.
/// </summary>
public sealed class AfskModem : IAprsModem, IAsyncDisposable
{
    private readonly IAudioStreamService _audioStream;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly IPttService _ptt;
    private readonly AfskSettings _settings;
    private readonly TxTiming _txTiming;
    private readonly AfskDemodulator _demodulator;
    private readonly ILogger<AfskModem> _logger;
    private readonly Lock _lifecycleLock = new();
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="AfskModem"/>.
    /// </summary>
    /// <param name="audioStream">The audio capture source. Must not be null.</param>
    /// <param name="audioPlayback">The audio playback sink. Must not be null.</param>
    /// <param name="ptt">The PTT control. Must not be null.</param>
    /// <param name="settings">The AFSK port configuration. Must not be null.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="txTiming">Optional channel-access timing helper; defaults to a shared instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if any required argument is null.</exception>
    public AfskModem(
        IAudioStreamService audioStream,
        IAudioPlaybackService audioPlayback,
        IPttService ptt,
        AfskSettings settings,
        ILoggerFactory? loggerFactory = null,
        TxTiming? txTiming = null)
    {
        ArgumentNullException.ThrowIfNull(audioStream);
        ArgumentNullException.ThrowIfNull(audioPlayback);
        ArgumentNullException.ThrowIfNull(ptt);
        ArgumentNullException.ThrowIfNull(settings);

        _audioStream = audioStream;
        _audioPlayback = audioPlayback;
        _ptt = ptt;
        _settings = settings;
        _txTiming = txTiming ?? new TxTiming();
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
    public async Task SendAsync(AprsPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var frameKey = $"{packet.Source}|{packet.Destination}|{packet.Raw}";

        // Holdoff: skip a frame we recently transmitted so a digipeated frame is not
        // immediately re-keyed (double-transmit suppression).
        if (_txTiming.ShouldHoldOff(frameKey, DateTimeOffset.UtcNow))
        {
            _logger.LogDebug("Suppressed duplicate AFSK transmission of {Source}.", packet.Source);
            return;
        }

        // Channel access: randomized slottime + persist-aloha backoff happens before
        // keying the transmitter, giving other stations a chance to use the channel.
        var channelAccessDelay = _txTiming.ComputeChannelAccessDelay(_settings.SlotTimeMs, _settings.Persist);
        if (channelAccessDelay > TimeSpan.Zero)
        {
            await Task.Delay(channelAccessDelay, cancellationToken).ConfigureAwait(false);
        }

        // APRS → AX.25 → HDLC → AFSK PCM.
        var ax25Data = Ax25Serializer.Serialize(packet);
        var bits = HdlcFramer.Encode(ax25Data);
        var samples = AfskModulator.Modulate(bits, _audioPlayback.SampleRate, _settings.AudioLevel);

        bool pttKeyed = _settings.PttMode != PttMode.None;
        if (pttKeyed)
        {
            _ptt.Engage();
        }

        try
        {
            if (_settings.TxDelayMs > 0)
            {
                await Task.Delay(_settings.TxDelayMs, cancellationToken).ConfigureAwait(false);
            }

            await _audioPlayback.PlayAsync(samples, cancellationToken).ConfigureAwait(false);

            if (_settings.TxTailMs > 0)
            {
                await Task.Delay(_settings.TxTailMs, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (pttKeyed)
            {
                _ptt.Release();
            }
        }

        _txTiming.MarkTransmitted(frameKey, DateTimeOffset.UtcNow);

        _logger.LogDebug(
            "Transmitted APRS packet via AFSK: Type={PacketType}, Source={Source}",
            packet.GetType().Name,
            packet.Source);
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

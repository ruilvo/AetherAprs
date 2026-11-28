// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;

using AetherAprs.Services.Platform;

using Android.Content;
using Android.Media;

using Microsoft.Extensions.Logging;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Observes the Android audio route and audio focus and feeds them into the shared
/// <see cref="AudioRouteMonitor"/>.
/// </summary>
/// <remarks>
/// This is the production writer of the snapshot the transmit gate authorizes
/// against. It registers an <see cref="AudioDeviceCallback"/> for output devices, a
/// receiver for <see cref="AudioManager.ActionAudioBecomingNoisy"/> and a focus
/// listener, so a headset unplugged mid-frame (or a call taking over) turns into a
/// blocked snapshot instead of a gate that always passes. The classification policy
/// itself lives in <see cref="AudioRouteClassifier"/> and is unit-tested.
/// </remarks>
public sealed class AudioRoutePlatformSource : IDisposable
{
    private readonly AudioManager _audioManager;
    private readonly AudioRouteMonitor _monitor;
    private readonly ILogger<AudioRoutePlatformSource>? _logger;
    private readonly DeviceCallback _deviceCallback;
    private readonly BecomingNoisyReceiver _noisyReceiver;
    private readonly FocusListener _focusListener;
    private readonly AudioFocusRequestClass _focusRequest;
    private readonly Lock _focusLock = new();
    private int _focusHolders;
    private bool _disposed;

    /// <summary>Creates the source and starts observing the platform route.</summary>
    /// <param name="monitor">Monitor that receives the observations.</param>
    /// <param name="logger">Optional logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="monitor"/> is null.</exception>
    public AudioRoutePlatformSource(AudioRouteMonitor monitor, ILogger<AudioRoutePlatformSource>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        _monitor = monitor;
        _logger = logger;
        _audioManager = (AudioManager)global::Android.App.Application.Context
            .GetSystemService(Context.AudioService)!;

        _deviceCallback = new DeviceCallback(this);
        _noisyReceiver = new BecomingNoisyReceiver(this);
        _focusListener = new FocusListener(this);
        _focusRequest = CreateFocusRequest(_focusListener);

        _audioManager.RegisterAudioDeviceCallback(_deviceCallback, null);
        RefreshRoute();

        try
        {
            global::Android.App.Application.Context.RegisterReceiver(
                _noisyReceiver,
                new IntentFilter(AudioManager.ActionAudioBecomingNoisy),
                ReceiverFlags.NotExported);
        }
        catch (Exception ex)
        {
            // Losing the becoming-noisy broadcast must not stop the port from
            // starting; device callbacks still report the replacement route.
            _logger?.LogWarning(ex, "Failed to register the audio-becoming-noisy receiver.");
        }
    }

    /// <summary>Gets the current route and focus snapshot.</summary>
    public AudioRouteSnapshot CurrentSnapshot => _monitor.Current;

    /// <summary>
    /// Requests transient audio focus for a transmission, sharing one focus request
    /// between every concurrent user.
    /// </summary>
    /// <returns><see langword="true"/> when audio focus is held.</returns>
    public bool TryAcquireAudioFocus()
    {
        lock (_focusLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_focusHolders > 0)
            {
                _focusHolders++;
                return true;
            }

            AudioFocusRequest result = _audioManager.RequestAudioFocus(_focusRequest);
            bool isGranted = result == AudioFocusRequest.Granted;

            if (isGranted)
            {
                _focusHolders = 1;
            }

            _monitor.SetFocusGranted(isGranted);

            if (!isGranted)
            {
                _logger?.LogWarning("Audio focus was refused; transmission cannot start.");
            }

            return isGranted;
        }
    }

    /// <summary>Releases one audio focus request when its user is done.</summary>
    public void ReleaseAudioFocus()
    {
        lock (_focusLock)
        {
            if (_focusHolders == 0)
            {
                return;
            }

            _focusHolders--;
            if (_focusHolders > 0)
            {
                return;
            }

            _audioManager.AbandonAudioFocusRequest(_focusRequest);
            _monitor.SetFocusGranted(false);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_focusLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _focusHolders = 0;
        }

        TryUnregisterDeviceCallback();
        TryUnregisterNoisyReceiver();
        _monitor.Reset();
    }

    private void RefreshRoute()
    {
        List<AudioRouteKind> sinks = [];

        AudioDeviceInfo[]? devices = _audioManager.GetDevices(GetDevicesTargets.Outputs);
        if (devices is not null)
        {
            foreach (AudioDeviceInfo? device in devices)
            {
                if (device is not null && ToRouteKind(device.Type) is { } route)
                {
                    sinks.Add(route);
                }
            }
        }

        _monitor.SetRoute(AudioRouteClassifier.SelectSink(sinks));
    }

    private static AudioFocusRequestClass CreateFocusRequest(AudioManager.IOnAudioFocusChangeListener listener)
    {
        AudioAttributes.Builder attributesBuilder = new();
        attributesBuilder.SetUsage(AudioUsageKind.Media);
        attributesBuilder.SetContentType(AudioContentType.Speech);

        AudioAttributes attributes = attributesBuilder.Build()
            ?? throw new InvalidOperationException("Failed to describe the AFSK transmit audio attributes.");

        AudioFocusRequestClass.Builder requestBuilder = new(AudioFocus.GainTransient);
        requestBuilder.SetAudioAttributes(attributes);
        requestBuilder.SetOnAudioFocusChangeListener(listener);

        return requestBuilder.Build()
            ?? throw new InvalidOperationException("Failed to build the AFSK transmit audio focus request.");
    }

    private void OnBecomingNoisy()
    {
        _logger?.LogWarning("The audio route is becoming noisy; transmission is blocked until it settles.");
        _monitor.SetBecomingNoisy(true);
    }

    private void OnFocusChanged(bool isGranted)
    {
        lock (_focusLock)
        {
            if (!isGranted)
            {
                _focusHolders = 0;
            }
        }

        _monitor.SetFocusGranted(isGranted);

        if (!isGranted)
        {
            _logger?.LogWarning("Audio focus was taken away; AFSK transmission is blocked.");
        }
    }

    private void TryUnregisterDeviceCallback()
    {
        try
        {
            _audioManager.UnregisterAudioDeviceCallback(_deviceCallback);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to unregister the audio device callback.");
        }
    }

    private void TryUnregisterNoisyReceiver()
    {
        try
        {
            global::Android.App.Application.Context.UnregisterReceiver(_noisyReceiver);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to unregister the audio-becoming-noisy receiver.");
        }
    }

    private static AudioRouteKind? ToRouteKind(AudioDeviceType deviceType) => deviceType switch
    {
        AudioDeviceType.WiredHeadset => AudioRouteKind.WiredHeadset,
        AudioDeviceType.WiredHeadphones => AudioRouteKind.WiredHeadphones,
        AudioDeviceType.BluetoothSco => AudioRouteKind.Bluetooth,
        AudioDeviceType.BluetoothA2dp => AudioRouteKind.Bluetooth,
        AudioDeviceType.BleHeadset => AudioRouteKind.Bluetooth,
        AudioDeviceType.BleSpeaker => AudioRouteKind.Bluetooth,
        AudioDeviceType.UsbDevice => AudioRouteKind.Usb,
        AudioDeviceType.UsbHeadset => AudioRouteKind.Usb,
        AudioDeviceType.BuiltinSpeaker => AudioRouteKind.Speaker,
        AudioDeviceType.BuiltinSpeakerSafe => AudioRouteKind.Speaker,
        _ => null
    };

    private sealed class DeviceCallback(AudioRoutePlatformSource owner) : AudioDeviceCallback
    {
        public override void OnAudioDevicesAdded(AudioDeviceInfo[]? addedDevices) => owner.RefreshRoute();

        public override void OnAudioDevicesRemoved(AudioDeviceInfo[]? removedDevices) => owner.RefreshRoute();
    }

    private sealed class BecomingNoisyReceiver(AudioRoutePlatformSource owner) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent) => owner.OnBecomingNoisy();
    }

    private sealed class FocusListener(AudioRoutePlatformSource owner)
        : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        public void OnAudioFocusChange(AudioFocus focusChange) =>
            owner.OnFocusChanged(focusChange == AudioFocus.Gain);
    }
}

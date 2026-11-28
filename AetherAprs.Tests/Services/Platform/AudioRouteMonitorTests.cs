// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;

using Xunit;

namespace AetherAprs.Tests.Services.Platform;

/// <summary>
/// Regression tests for the production writer of the audio route snapshot: the
/// transmit gate must authorize and invalidate the snapshots the platform
/// observation actually produces, not ones a test fabricated.
/// </summary>
public sealed class AudioRouteMonitorTests
{
    [Fact]
    public void SetRoute_FirstRoute_AdvancesTheGeneration()
    {
        AudioRouteMonitor monitor = new();

        AudioRouteSnapshot snapshot = monitor.SetRoute(AudioRouteKind.WiredHeadset);

        Assert.Equal(AudioRouteKind.WiredHeadset, snapshot.Route);
        Assert.Equal(1, snapshot.Generation);
        Assert.False(snapshot.IsFocusGranted);
        Assert.False(snapshot.IsBecomingNoisy);
    }

    [Fact]
    public void SetRoute_SameRoute_KeepsTheGeneration()
    {
        AudioRouteMonitor monitor = new();
        AudioRouteSnapshot first = monitor.SetRoute(AudioRouteKind.Speaker);

        AudioRouteSnapshot second = monitor.SetRoute(AudioRouteKind.Speaker);

        Assert.Equal(first.Generation, second.Generation);
    }

    [Fact]
    public void UnpluggingAHeadset_MidFrame_AbortsTheAuthorizedTransmission()
    {
        AudioRouteMonitor monitor = new();
        AudioTransmitGate gate = new();

        monitor.SetRoute(AudioRouteKind.WiredHeadset);
        monitor.SetFocusGranted(true);
        Assert.Equal(AudioTransmitBlockReason.None, gate.TryKey(monitor.Current));

        // The platform reports ACTION_AUDIO_BECOMING_NOISY, then the replacement sink.
        monitor.SetBecomingNoisy(true);
        Assert.Equal(AudioTransmitBlockReason.RouteBecomingNoisy, gate.CheckBeforeTransmit(monitor.Current));
        Assert.True(gate.IsAborted);

        monitor.SetRoute(AudioRouteKind.Speaker);
        AudioRouteSnapshot afterUnplug = monitor.Current;
        Assert.False(afterUnplug.IsBecomingNoisy);
        Assert.Equal(AudioRouteKind.Speaker, afterUnplug.Route);

        // The unplug is a different route generation: the authorization granted for
        // the headset is not silently reused for the speaker.
        gate.Release();
        monitor.SetFocusGranted(true);
        Assert.Equal(AudioTransmitBlockReason.None, gate.TryKey(afterUnplug));
        Assert.Equal(
            AudioTransmitBlockReason.RouteChanged,
            gate.CheckBeforeTransmit(monitor.SetRoute(AudioRouteKind.WiredHeadphones)));
    }

    [Fact]
    public void LosingAudioFocus_BlocksTheProducedSnapshot()
    {
        AudioRouteMonitor monitor = new();
        monitor.SetRoute(AudioRouteKind.Speaker);
        monitor.SetFocusGranted(true);
        AudioTransmitGate gate = new();
        Assert.Equal(AudioTransmitBlockReason.None, gate.TryKey(monitor.Current));

        monitor.SetFocusGranted(false);

        Assert.Equal(AudioTransmitBlockReason.FocusLost, gate.CheckBeforeTransmit(monitor.Current));
        Assert.Equal(AudioTransmitBlockReason.FocusLost, AudioRouteValidator.Evaluate(monitor.Current));
    }

    [Fact]
    public void Reset_InvalidatesAnAuthorizationGrantedBeforeIt()
    {
        AudioRouteMonitor monitor = new();
        monitor.SetRoute(AudioRouteKind.Speaker);
        monitor.SetFocusGranted(true);
        AudioTransmitGate gate = new();
        Assert.Equal(AudioTransmitBlockReason.None, gate.TryKey(monitor.Current));

        monitor.Reset();

        // After a reset nothing observed still holds: focus is gone first, and the
        // route is unknown, so the stale authorization cannot be reused either way.
        Assert.Equal(AudioTransmitBlockReason.FocusLost, gate.CheckBeforeTransmit(monitor.Current));
        Assert.True(gate.IsAborted);
        Assert.Equal(AudioRouteKind.Unknown, monitor.Current.Route);
    }

    [Fact]
    public void SelectSink_PrefersWiredHeadsetThenBluetoothThenUsb()
    {
        Assert.Equal(
            AudioRouteKind.WiredHeadset,
            AudioRouteClassifier.SelectSink([
                AudioRouteKind.Speaker,
                AudioRouteKind.Bluetooth,
                AudioRouteKind.WiredHeadset,
                AudioRouteKind.Usb
            ]));

        Assert.Equal(
            AudioRouteKind.Bluetooth,
            AudioRouteClassifier.SelectSink([AudioRouteKind.Speaker, AudioRouteKind.Bluetooth, AudioRouteKind.Usb]));

        Assert.Equal(
            AudioRouteKind.Usb,
            AudioRouteClassifier.SelectSink([AudioRouteKind.Usb, AudioRouteKind.WiredHeadphones]));

        Assert.Equal(
            AudioRouteKind.Speaker,
            AudioRouteClassifier.SelectSink([AudioRouteKind.Speaker]));
    }

    [Fact]
    public void SelectSink_NoUsableSink_ReportsUnknown()
    {
        Assert.Equal(AudioRouteKind.Unknown, AudioRouteClassifier.SelectSink([]));
        Assert.Equal(AudioRouteKind.Unknown, AudioRouteClassifier.SelectSink([AudioRouteKind.Unknown]));
    }
}

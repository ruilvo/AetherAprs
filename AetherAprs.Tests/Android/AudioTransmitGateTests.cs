// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Android.Services.Platform;
using Xunit;

namespace AetherAprs.Tests.Android;

/// <summary>
/// Regression tests: a transmission that was authorized on one audio route must be
/// stopped, and the transmitter unkeyed, when the route changes or the focus is lost
/// while the frame is going out.
/// </summary>
public sealed class AudioTransmitGateTests
{
    private static AudioRouteSnapshot Snapshot(
        AudioRouteKind route = AudioRouteKind.Speaker,
        bool isFocusGranted = true,
        bool isBecomingNoisy = false,
        int generation = 1) =>
        new(route, isFocusGranted, isBecomingNoisy, generation);

    [Fact]
    public void TryKey_ValidSnapshot_KeysTransmitter()
    {
        AudioTransmitGate gate = new();

        AudioTransmitBlockReason reason = gate.TryKey(Snapshot());

        Assert.Equal(AudioTransmitBlockReason.None, reason);
        Assert.True(gate.IsKeyed);
        Assert.False(gate.IsAborted);
        Assert.Equal(AudioTransmitState.Keyed, gate.State);
    }

    [Fact]
    public void TryKey_FocusLost_RefusesAndStaysIdle()
    {
        AudioTransmitGate gate = new();

        AudioTransmitBlockReason reason = gate.TryKey(Snapshot(isFocusGranted: false));

        Assert.Equal(AudioTransmitBlockReason.FocusLost, reason);
        Assert.False(gate.IsKeyed);
        Assert.Equal(AudioTransmitState.Idle, gate.State);
    }

    [Fact]
    public void TryKey_WhileKeyed_RevalidatesInsteadOfReauthorizing()
    {
        AudioTransmitGate gate = new();
        gate.TryKey(Snapshot(generation: 1));

        // A route switch between keying and the second call cannot silently re-authorize.
        AudioTransmitBlockReason reason = gate.TryKey(Snapshot(route: AudioRouteKind.Bluetooth, generation: 2));

        Assert.Equal(AudioTransmitBlockReason.RouteChanged, reason);
        Assert.True(gate.IsAborted);
        Assert.False(gate.IsKeyed);
    }

    [Fact]
    public void CheckBeforeTransmit_SameGeneration_KeepsTransmitting()
    {
        AudioTransmitGate gate = new();
        gate.TryKey(Snapshot(generation: 4));

        Assert.Equal(AudioTransmitBlockReason.None, gate.CheckBeforeTransmit(Snapshot(generation: 4)));
        Assert.True(gate.IsKeyed);
    }

    [Fact]
    public void CheckBeforeTransmit_HeadsetUnpluggedMidFrame_Aborts()
    {
        AudioTransmitGate gate = new();
        gate.TryKey(Snapshot(route: AudioRouteKind.WiredHeadset, generation: 1));

        AudioTransmitBlockReason reason = gate.CheckBeforeTransmit(
            Snapshot(route: AudioRouteKind.Speaker, isBecomingNoisy: true, generation: 2));

        Assert.Equal(AudioTransmitBlockReason.RouteBecomingNoisy, reason);
        Assert.True(gate.IsAborted);
        Assert.Equal(AudioTransmitBlockReason.RouteBecomingNoisy, gate.LastBlockReason);
    }

    [Fact]
    public void CheckBeforeTransmit_FocusTakenByCall_Aborts()
    {
        AudioTransmitGate gate = new();
        gate.TryKey(Snapshot(generation: 2));

        AudioTransmitBlockReason reason = gate.CheckBeforeTransmit(
            Snapshot(isFocusGranted: false, generation: 2));

        Assert.Equal(AudioTransmitBlockReason.FocusLost, reason);
        Assert.True(gate.IsAborted);
    }

    [Fact]
    public void CheckBeforeTransmit_WhenIdle_DoesNotGrantAuthorization()
    {
        AudioTransmitGate gate = new();

        // Nothing was keyed, so a stray check neither permits nor authorizes anything.
        Assert.Equal(AudioTransmitBlockReason.None, gate.CheckBeforeTransmit(Snapshot()));
        Assert.False(gate.IsKeyed);

        // The next key still has to be authorized from scratch, and a route switch
        // made in between is not laundered by the earlier check.
        gate.TryKey(Snapshot(route: AudioRouteKind.Bluetooth, generation: 2));
        Assert.Equal(
            AudioTransmitBlockReason.RouteChanged,
            gate.CheckBeforeTransmit(Snapshot(route: AudioRouteKind.Usb, generation: 3)));
    }

    [Fact]
    public void CheckBeforeTransmit_AfterAbort_StaysBlocked()
    {
        AudioTransmitGate gate = new();
        gate.TryKey(Snapshot(generation: 1));
        gate.CheckBeforeTransmit(Snapshot(route: AudioRouteKind.Bluetooth, generation: 2));

        // An aborted transmission is not silently resumed on the new route.
        AudioTransmitBlockReason reason = gate.CheckBeforeTransmit(
            Snapshot(route: AudioRouteKind.Bluetooth, generation: 2));

        Assert.Equal(AudioTransmitBlockReason.RouteChanged, reason);
        Assert.True(gate.IsAborted);
    }

    [Fact]
    public void Release_AfterAbort_AllowsFreshAuthorization()
    {
        AudioTransmitGate gate = new();
        gate.TryKey(Snapshot(generation: 1));
        gate.CheckBeforeTransmit(Snapshot(route: AudioRouteKind.Bluetooth, generation: 2));

        gate.Release();

        Assert.Equal(AudioTransmitState.Idle, gate.State);
        Assert.Equal(AudioTransmitBlockReason.None, gate.TryKey(
            Snapshot(route: AudioRouteKind.Bluetooth, generation: 2)));
    }

    [Fact]
    public void Release_ThenRouteSwitch_StopsTheNextFrameToo()
    {
        AudioTransmitGate gate = new();
        gate.TryKey(Snapshot(route: AudioRouteKind.Speaker, generation: 1));
        gate.Release();

        // Keying again on the new route is a fresh authorization; a further switch
        // before the frame is written still aborts it, so releasing does not leave a
        // stale generation behind.
        Assert.Equal(
            AudioTransmitBlockReason.None,
            gate.TryKey(Snapshot(route: AudioRouteKind.Bluetooth, generation: 2)));

        Assert.Equal(
            AudioTransmitBlockReason.RouteChanged,
            gate.CheckBeforeTransmit(Snapshot(route: AudioRouteKind.Speaker, generation: 3)));
    }
}

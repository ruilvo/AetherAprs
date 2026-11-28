// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;

using Xunit;

namespace AetherAprs.Tests.Services.Platform;

/// <summary>
/// Regression tests: a route switch, a focus loss or a headset being unplugged
/// must invalidate the authorization granted for the previous audio route.
/// </summary>
public sealed class AudioRouteValidatorTests
{
    private static AudioRouteSnapshot Snapshot(
        AudioRouteKind route = AudioRouteKind.Speaker,
        bool isFocusGranted = true,
        bool isBecomingNoisy = false,
        int generation = 0) =>
        new(route, isFocusGranted, isBecomingNoisy, generation);

    [Fact]
    public void Authorize_ValidSnapshot_AllowsTransmission()
    {
        AudioRouteValidator validator = new();

        AudioTransmitBlockReason reason = validator.Authorize(Snapshot());

        Assert.Equal(AudioTransmitBlockReason.None, reason);
        Assert.True(validator.IsAuthorized);
    }

    [Fact]
    public void Authorize_FocusLost_BlocksAndDoesNotAuthorize()
    {
        AudioRouteValidator validator = new();

        AudioTransmitBlockReason reason = validator.Authorize(Snapshot(isFocusGranted: false));

        Assert.Equal(AudioTransmitBlockReason.FocusLost, reason);
        Assert.False(validator.IsAuthorized);
    }

    [Fact]
    public void Authorize_RouteBecomingNoisy_Blocks()
    {
        AudioRouteValidator validator = new();

        Assert.Equal(
            AudioTransmitBlockReason.RouteBecomingNoisy,
            validator.Authorize(Snapshot(isBecomingNoisy: true)));
    }

    [Fact]
    public void Authorize_UnknownRoute_Blocks()
    {
        AudioRouteValidator validator = new();

        Assert.Equal(
            AudioTransmitBlockReason.RouteUnknown,
            validator.Authorize(Snapshot(route: AudioRouteKind.Unknown)));
    }

    [Fact]
    public void Revalidate_SameGeneration_AllowsTransmission()
    {
        AudioRouteValidator validator = new();
        AudioRouteSnapshot snapshot = Snapshot(generation: 3);
        validator.Authorize(snapshot);

        Assert.Equal(AudioTransmitBlockReason.None, validator.Revalidate(snapshot));
    }

    [Fact]
    public void Revalidate_RouteSwitch_InvalidatesPreviousAuthorization()
    {
        AudioRouteValidator validator = new();
        validator.Authorize(Snapshot(route: AudioRouteKind.Speaker, generation: 1));

        AudioTransmitBlockReason reason = validator.Revalidate(
            Snapshot(route: AudioRouteKind.Bluetooth, generation: 2));

        Assert.Equal(AudioTransmitBlockReason.RouteChanged, reason);
        Assert.False(validator.IsAuthorized);
    }

    [Fact]
    public void Revalidate_FocusLoss_BlocksTransmission()
    {
        AudioRouteValidator validator = new();
        AudioRouteSnapshot snapshot = Snapshot(generation: 1);
        validator.Authorize(snapshot);

        AudioTransmitBlockReason reason = validator.Revalidate(
            snapshot with { IsFocusGranted = false });

        Assert.Equal(AudioTransmitBlockReason.FocusLost, reason);
        Assert.False(validator.IsAuthorized);
    }

    [Fact]
    public void Authorize_RestoredRouteAndFocus_AllowsTransmissionAgain()
    {
        AudioRouteValidator validator = new();
        validator.Authorize(Snapshot(generation: 1));
        validator.Revalidate(Snapshot(route: AudioRouteKind.Bluetooth, generation: 2));

        AudioRouteSnapshot restored = Snapshot(route: AudioRouteKind.Speaker, generation: 3);
        AudioTransmitBlockReason reason = validator.Authorize(restored);

        Assert.Equal(AudioTransmitBlockReason.None, reason);
        Assert.Equal(AudioTransmitBlockReason.None, validator.Revalidate(restored));
    }
}

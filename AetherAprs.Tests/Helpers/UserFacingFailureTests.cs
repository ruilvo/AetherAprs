// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using AetherAprs.Helpers;
using AetherAprs.Localization;
using AetherAprs.Models.Objects;
using Xunit;

namespace AetherAprs.Tests.Helpers;

/// <summary>
/// The operator must never see a provider message: the text on screen has to be a
/// localized sentence that names the recovery action.
/// </summary>
public sealed class UserFacingFailureTests
{
    [Fact]
    public void ABlockedPermissionPointsAtTheApplicationSettings()
    {
        string text = UserFacingFailure.Describe(new UnauthorizedAccessException("Bluetooth permissions were not granted."));

        Assert.Equal(Strings.Get("FailurePermissionBlocked"), text);
        Assert.DoesNotContain("Bluetooth permissions", text);
    }

    [Fact]
    public void AnUnavailableActionKeepsTheProviderMessageOutOfTheText()
    {
        string text = UserFacingFailure.Describe(
            new InvalidOperationException("Bluetooth LE scanner is not available on this device."));

        Assert.Equal(Strings.Get("FailureUnavailable"), text);
        Assert.DoesNotContain("Bluetooth LE scanner", text);
    }

    [Fact]
    public void AFullVolumeIsReportedAsStorage()
    {
        Assert.Equal(
            Strings.Get("FailureStorageUnavailable"),
            UserFacingFailure.Describe(new IOException("No space left on device")));
    }

    [Fact]
    public void ACancellationIsNotReportedAsAnError()
    {
        Assert.Equal(
            Strings.Get("FailureCancelled"),
            UserFacingFailure.Describe(new OperationCanceledException()));
    }

    [Fact]
    public void AnUnknownFailureStaysGeneric()
    {
        Assert.Equal(
            Strings.Get("FailureUnexpected"),
            UserFacingFailure.Describe(new NotFiniteNumberException()));
    }

    [Theory]
    [InlineData(BroadcastFailure.NoTransmitRoute, "FailureNoTransmitRoute")]
    [InlineData(BroadcastFailure.UnknownObject, "FailureUnknownObject")]
    [InlineData(BroadcastFailure.TransmissionRejected, "FailureTransmissionRejected")]
    public void ATypedBroadcastFailureDescribesItsOwnReason(
        BroadcastFailure failure, string expectedKey)
    {
        string text = UserFacingFailure.Describe(new BroadcastFailedException(failure));

        // The typed reason is more specific than the generic category its base type
        // would map to, so it wins.
        Assert.Equal(Strings.Get(expectedKey), text);
        Assert.NotEqual(Strings.Get("FailureUnavailable"), text);
    }

    [Fact]
    public void BroadcastFailureNoneHasNoText()
    {
        Assert.Equal(string.Empty, BroadcastFailureText.Describe(BroadcastFailure.None));
    }

    [Fact]
    public void EveryCategoryResolvesToARealResource()
    {
        Exception[] samples =
        [
            new UnauthorizedAccessException(),
            new InvalidOperationException(),
            new IOException(),
            new OperationCanceledException(),
            new NotFiniteNumberException(),
        ];

        foreach (Exception sample in samples)
        {
            string key = UserFacingFailure.Category(sample);

            // Strings.Get returns the key itself when the resource is missing, so an
            // unresolved key would surface as its own name.
            Assert.NotEqual(key, Strings.Get(key));
            Assert.False(string.IsNullOrWhiteSpace(Strings.Get(key)));
        }
    }
}

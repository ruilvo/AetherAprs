// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.Services.Platform;

using Xunit;

namespace AetherAprs.Tests.Services.Platform;

public sealed class PermissionRequestPolicyTests
{
    [Fact]
    public void Evaluate_AGrantedPermission_IsGrantedRegardlessOfTheOtherFacts()
    {
        PermissionRequestResult result = PermissionRequestPolicy.Evaluate(
            PermissionKind.Location,
            granted: true,
            rationaleSuggested: true,
            requestedBefore: true);

        Assert.Equal(PermissionOutcome.Granted, result.Outcome);
        Assert.Equal(PermissionKind.Location, result.Kind);
        Assert.True(result.IsGranted);
        Assert.False(result.CanPromptAgain);
        Assert.False(result.RequiresApplicationSettings);
    }

    [Fact]
    public void Evaluate_AnUnsupportedPermission_IsUnavailableEvenWhenDenied()
    {
        PermissionRequestResult result = PermissionRequestPolicy.Evaluate(
            PermissionKind.Notifications,
            granted: false,
            rationaleSuggested: false,
            requestedBefore: true,
            supported: false);

        Assert.Equal(PermissionOutcome.Unavailable, result.Outcome);
        Assert.False(result.IsGranted);
        Assert.False(result.CanPromptAgain);
        Assert.False(result.RequiresApplicationSettings);
    }

    [Fact]
    public void Evaluate_ADenialThatStillSuggestsARationale_CanBePromptedAgain()
    {
        PermissionRequestResult result = PermissionRequestPolicy.Evaluate(
            PermissionKind.Audio,
            granted: false,
            rationaleSuggested: true,
            requestedBefore: true);

        Assert.Equal(PermissionOutcome.Denied, result.Outcome);
        Assert.True(result.CanPromptAgain);
        Assert.False(result.RequiresApplicationSettings);
    }

    [Fact]
    public void Evaluate_AFIRSTDenialWithoutRationale_IsNotTreatedAsPermanent()
    {
        // Before any request the platform also reports "no rationale", so the flag
        // alone cannot mean the prompt will never appear again.
        PermissionRequestResult result = PermissionRequestPolicy.Evaluate(
            PermissionKind.Bluetooth,
            granted: false,
            rationaleSuggested: false,
            requestedBefore: false);

        Assert.Equal(PermissionOutcome.Denied, result.Outcome);
        Assert.True(result.CanPromptAgain);
        Assert.False(result.RequiresApplicationSettings);
    }

    [Fact]
    public void Evaluate_ADenialWithoutRationaleAfterAnEarlierRequest_NeedsApplicationSettings()
    {
        PermissionRequestResult result = PermissionRequestPolicy.Evaluate(
            PermissionKind.Bluetooth,
            granted: false,
            rationaleSuggested: false,
            requestedBefore: true);

        Assert.Equal(PermissionOutcome.PermanentlyDenied, result.Outcome);
        Assert.False(result.IsGranted);
        Assert.False(result.CanPromptAgain);
        Assert.True(result.RequiresApplicationSettings);
    }

    [Fact]
    public void Evaluate_EveryOutcomeKeepsTheRequestedKind()
    {
        PermissionRequestResult[] results =
        [
            PermissionRequestResult.Granted(PermissionKind.Notifications),
            PermissionRequestResult.Denied(PermissionKind.Location),
            PermissionRequestResult.PermanentlyDenied(PermissionKind.BatteryOptimization),
            PermissionRequestResult.Unavailable(PermissionKind.Audio)
        ];

        Assert.Equal(PermissionKind.Notifications, results[0].Kind);
        Assert.Equal(PermissionKind.Location, results[1].Kind);
        Assert.Equal(PermissionKind.BatteryOptimization, results[2].Kind);
        Assert.Equal(PermissionKind.Audio, results[3].Kind);
        Assert.Equal(
            [
                PermissionOutcome.Granted,
                PermissionOutcome.Denied,
                PermissionOutcome.PermanentlyDenied,
                PermissionOutcome.Unavailable
            ],
            Array.ConvertAll(results, result => result.Outcome));
    }
}

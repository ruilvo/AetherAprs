// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

using AetherAprs.Services.Platform;

using Xunit;

namespace AetherAprs.Tests.Services.Platform;

public sealed class PermissionGrantTrackerTests
{
    [Fact]
    public void Observe_FirstRead_IsNotATransition()
    {
        PermissionGrantTracker tracker = new();
        List<PermissionGrantTransition> changes = [];
        tracker.Changed += (_, transition) => changes.Add(transition);

        PermissionGrantTransition observed = tracker.Observe(
            PermissionKind.Notifications,
            PermissionGrant.Granted,
            rationaleSuggested: false,
            requestedBefore: true);

        Assert.Equal(PermissionOutcome.Granted, observed.Result.Outcome);
        Assert.Null(observed.Previous);
        Assert.False(observed.HasChanged);
        Assert.Empty(changes);
    }

    [Fact]
    public void Observe_WhenAGrantIsRevokedAndTheRationaleStopsShowing_ReportsPermanentDenial()
    {
        PermissionGrantTracker tracker = new();
        tracker.Observe(PermissionKind.Location, PermissionGrant.Granted, rationaleSuggested: false, requestedBefore: true);
        List<PermissionGrantTransition> changes = [];
        tracker.Changed += (_, transition) => changes.Add(transition);

        PermissionGrantTransition revoked = tracker.Observe(
            PermissionKind.Location,
            PermissionGrant.Denied,
            rationaleSuggested: false,
            requestedBefore: true);

        Assert.Equal(PermissionOutcome.PermanentlyDenied, revoked.Result.Outcome);
        Assert.True(revoked.Result.RequiresApplicationSettings);
        Assert.Equal(PermissionOutcome.Granted, revoked.Previous);
        Assert.True(revoked.HasChanged);
        PermissionGrantTransition change = Assert.Single(changes);
        Assert.Equal(PermissionKind.Location, change.Kind);
        Assert.Equal(PermissionOutcome.PermanentlyDenied, change.Result.Outcome);
    }

    [Fact]
    public void Observe_WhenTheOperatorGrantsThePermissionInSettings_ReportsGranted()
    {
        PermissionGrantTracker tracker = new();
        tracker.Observe(PermissionKind.Audio, PermissionGrant.Denied, rationaleSuggested: true, requestedBefore: true);
        List<PermissionGrantTransition> changes = [];
        tracker.Changed += (_, transition) => changes.Add(transition);

        PermissionGrantTransition restored = tracker.Observe(
            PermissionKind.Audio,
            PermissionGrant.Granted,
            rationaleSuggested: false,
            requestedBefore: true);

        Assert.True(restored.Result.IsGranted);
        Assert.Equal(PermissionOutcome.Denied, restored.Previous);
        Assert.True(restored.HasChanged);
        Assert.Single(changes);
        Assert.Equal(PermissionOutcome.Granted, tracker.LastKnown(PermissionKind.Audio));
    }

    [Fact]
    public void Observe_WhenTheStateIsUnchanged_DoesNotRaiseChanged()
    {
        PermissionGrantTracker tracker = new();
        tracker.Observe(PermissionKind.Bluetooth, PermissionGrant.Granted, rationaleSuggested: false, requestedBefore: true);
        List<PermissionGrantTransition> changes = [];
        tracker.Changed += (_, transition) => changes.Add(transition);

        PermissionGrantTransition again = tracker.Observe(
            PermissionKind.Bluetooth,
            PermissionGrant.Granted,
            rationaleSuggested: false,
            requestedBefore: true);

        Assert.False(again.HasChanged);
        Assert.Empty(changes);
    }

    [Fact]
    public void Observe_WhenAFreshDenialStillOffersARationale_ReportsDenied()
    {
        PermissionGrantTracker tracker = new();

        PermissionGrantTransition observed = tracker.Observe(
            PermissionKind.Notifications,
            PermissionGrant.Denied,
            rationaleSuggested: false,
            requestedBefore: false);

        Assert.Equal(PermissionOutcome.Denied, observed.Result.Outcome);
        Assert.True(observed.Result.CanPromptAgain);
    }

    [Fact]
    public void Observe_WhenThePlatformCannotReport_ReportsUnavailable()
    {
        PermissionGrantTracker tracker = new();

        PermissionGrantTransition observed = tracker.Observe(
            PermissionKind.BatteryOptimization,
            PermissionGrant.Unsupported,
            rationaleSuggested: false,
            requestedBefore: false);

        Assert.Equal(PermissionOutcome.Unavailable, observed.Result.Outcome);
        Assert.False(observed.Result.IsGranted);
        Assert.Equal(PermissionOutcome.Unavailable, tracker.LastKnown(PermissionKind.BatteryOptimization));
    }

    [Fact]
    public void LastKnown_ForAPermissionThatWasNeverRead_IsNull()
    {
        PermissionGrantTracker tracker = new();

        Assert.Null(tracker.LastKnown(PermissionKind.Location));
    }
}

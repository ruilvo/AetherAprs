// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using AetherAprs.Services.Platform;
using Xunit;

namespace AetherAprs.Tests.Services.Platform;

/// <summary>
/// A resumed activity re-reads the grants without prompting. Those reads used to be
/// logged and dropped, so the home banner kept whatever the last prompt said.
/// </summary>
public sealed class PermissionResumeReportTests
{
    [Fact]
    public void RevokingAGrantInTheSystemSettingsRaisesTheWarning()
    {
        PermissionAttentionService attention = new();

        PermissionResumeReport.Apply(
            [Transition(PermissionKind.Bluetooth, PermissionOutcome.Granted, PermissionOutcome.PermanentlyDenied)],
            attention);

        PermissionAttention outstanding = Assert.Single(attention.Outstanding);
        Assert.Equal(PermissionKind.Bluetooth, outstanding.Kind);
        Assert.True(outstanding.RequiresApplicationSettings);
    }

    [Fact]
    public void RestoringAGrantInTheSystemSettingsClearsTheWarning()
    {
        PermissionAttentionService attention = new();
        attention.Report(PermissionRequestResult.PermanentlyDenied(PermissionKind.Bluetooth));
        Assert.Single(attention.Outstanding);

        PermissionResumeReport.Apply(
            [Transition(PermissionKind.Bluetooth, PermissionOutcome.PermanentlyDenied, PermissionOutcome.Granted)],
            attention);

        Assert.Empty(attention.Outstanding);
    }

    [Fact]
    public void AFirstReadIsNotReported()
    {
        PermissionAttentionService attention = new();

        IReadOnlyList<PermissionGrantTransition> changed = PermissionResumeReport.Apply(
            [new PermissionGrantTransition(
                PermissionKind.Audio,
                Previous: null,
                PermissionRequestResult.Denied(PermissionKind.Audio))],
            attention);

        // Nothing changed: the process simply learned the permission for the first
        // time, which the operator did not do.
        Assert.Empty(changed);
        Assert.Empty(attention.Outstanding);
    }

    [Fact]
    public void AnUnchangedOutcomeIsNotReported()
    {
        PermissionAttentionService attention = new();

        IReadOnlyList<PermissionGrantTransition> changed = PermissionResumeReport.Apply(
            [Transition(PermissionKind.Audio, PermissionOutcome.Denied, PermissionOutcome.Denied)],
            attention);

        Assert.Empty(changed);
        Assert.Empty(attention.Outstanding);
    }

    [Fact]
    public void EveryChangedTransitionIsReturnedForLogging()
    {
        IReadOnlyList<PermissionGrantTransition> transitions =
        [
            Transition(PermissionKind.Notifications, PermissionOutcome.Granted, PermissionOutcome.Denied),
            new PermissionGrantTransition(
                PermissionKind.Audio,
                Previous: null,
                PermissionRequestResult.Unavailable(PermissionKind.Audio)),
            Transition(PermissionKind.Location, PermissionOutcome.Denied, PermissionOutcome.Granted),
        ];

        IReadOnlyList<PermissionGrantTransition> changed = PermissionResumeReport.Apply(transitions, attention: null);

        Assert.Equal(
            [PermissionKind.Notifications, PermissionKind.Location],
            changed.Select(transition => transition.Kind));
    }

    [Fact]
    public void AMissingReporterStillClassifiesTheTransitions()
    {
        // The Android entry point may run before the service provider is ready.
        IReadOnlyList<PermissionGrantTransition> changed = PermissionResumeReport.Apply(
            [Transition(PermissionKind.Bluetooth, PermissionOutcome.Granted, PermissionOutcome.Denied)],
            attention: null);

        Assert.Single(changed);
    }

    private static PermissionGrantTransition Transition(
        PermissionKind kind,
        PermissionOutcome previous,
        PermissionOutcome current) =>
        new(kind, previous, new PermissionRequestResult(kind, current));
}

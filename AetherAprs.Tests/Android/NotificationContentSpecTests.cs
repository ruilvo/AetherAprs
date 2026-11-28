// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Android.Services.Platform;

using Xunit;

namespace AetherAprs.Tests.Android;

/// <summary>
/// Tests the platform-neutral notification descriptor: the icon must be the
/// drawable the manifests and adapters reference, and the foreground
/// notification must keep its stable Stop action.
/// </summary>
public sealed class NotificationContentSpecTests
{
    [Fact]
    public void Foreground_UsesSharedNotificationIcon()
    {
        NotificationContentSpec spec = NotificationSpecs.Foreground("Title", "Body", "Close");

        Assert.Equal("ic_notification", spec.IconResourceName);
        Assert.Equal(NotificationSpecs.NotificationIconResourceName, spec.IconResourceName);
    }

    [Fact]
    public void Foreground_UsesForegroundChannelAndStaysOngoing()
    {
        NotificationContentSpec spec = NotificationSpecs.Foreground("Title", "Body", "Close");

        Assert.Equal("aetheraprs_foreground", spec.ChannelId);
        Assert.False(spec.AutoCancel);
        Assert.Equal("Title", spec.Title);
        Assert.Equal("Body", spec.Text);
    }

    [Fact]
    public void Foreground_HasSingleStopActionWithStableId()
    {
        NotificationContentSpec spec = NotificationSpecs.Foreground("Title", "Body", "Close");

        Assert.Single(spec.Actions);
        Assert.Equal("STOP_ALL_PORTS", spec.Actions[0].Id);
        Assert.Equal(NotificationSpecs.StopAllActionId, spec.Actions[0].Id);
        Assert.Equal("Close", spec.Actions[0].Label);
    }

    [Fact]
    public void Foreground_ActionIdIsStableAcrossBuilds()
    {
        NotificationContentSpec first = NotificationSpecs.Foreground("First", "Body", "Close");
        NotificationContentSpec second = NotificationSpecs.Foreground("Second", "Body", "Close");

        Assert.Equal(first.Actions[0].Id, second.Actions[0].Id);
    }

    [Fact]
    public void ForegroundTimeout_IsAutoCancellingAlertWithoutActions()
    {
        NotificationContentSpec spec = NotificationSpecs.ForegroundTimeout("Timeout", "Explanation");

        Assert.Equal("aetheraprs_alerts", spec.ChannelId);
        Assert.Equal("ic_notification", spec.IconResourceName);
        Assert.True(spec.AutoCancel);
        Assert.Empty(spec.Actions);
    }

    [Fact]
    public void Message_IsAutoCancellingWithMessageChannel()
    {
        NotificationContentSpec spec = NotificationSpecs.Message("PE1ABC", "Hello");

        Assert.Equal("aetheraprs_messages", spec.ChannelId);
        Assert.Equal("ic_notification", spec.IconResourceName);
        Assert.True(spec.AutoCancel);
        Assert.Empty(spec.Actions);
    }
}

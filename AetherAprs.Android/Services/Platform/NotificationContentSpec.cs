// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace AetherAprs.Android.Services.Platform;

/// <summary>A notification action button.</summary>
/// <param name="Id">Stable action identifier delivered to the service or receiver.</param>
/// <param name="Label">Localized button label.</param>
internal readonly record struct NotificationActionSpec(string Id, string Label);

/// <summary>
/// Platform-neutral description of a notification's channel, icon, text and
/// actions. Kept free of Android types so the icon and action wiring can be
/// unit-tested.
/// </summary>
/// <param name="ChannelId">Notification channel the content is posted on.</param>
/// <param name="Title">Localized notification title.</param>
/// <param name="Text">Localized notification body.</param>
/// <param name="IconResourceName">Drawable resource name for the small icon.</param>
/// <param name="AutoCancel">Whether tapping the notification dismisses it.</param>
/// <param name="Actions">Action buttons in display order.</param>
internal sealed record NotificationContentSpec(
    string ChannelId,
    string Title,
    string Text,
    string IconResourceName,
    bool AutoCancel,
    IReadOnlyList<NotificationActionSpec> Actions);

/// <summary>
/// Builds the notification descriptors the Android adapters render. Strings and
/// resource names are plain values; the Android layer resolves them.
/// </summary>
internal static class NotificationSpecs
{
    /// <summary>Drawable resource name used for every notification's small icon.</summary>
    internal const string NotificationIconResourceName = "ic_notification";

    /// <summary>Channel for the ongoing foreground-service notification.</summary>
    internal const string ForegroundChannelId = "aetheraprs_foreground";

    /// <summary>Channel for foreground-service timeout alerts.</summary>
    internal const string AlertChannelId = "aetheraprs_alerts";

    /// <summary>Channel for incoming message notifications.</summary>
    internal const string MessageChannelId = "aetheraprs_messages";

    /// <summary>
    /// Stable identifier of the "stop all ports" action. It is also the intent
    /// action the foreground service handles, so changing it breaks the button.
    /// </summary>
    internal const string StopAllActionId = "STOP_ALL_PORTS";

    private static readonly NotificationActionSpec[] NoActions = Array.Empty<NotificationActionSpec>();

    /// <summary>Builds the ongoing foreground-service notification.</summary>
    /// <param name="title">Localized title.</param>
    /// <param name="text">Localized body.</param>
    /// <param name="stopLabel">Localized stop-action label.</param>
    public static NotificationContentSpec Foreground(string title, string text, string stopLabel) =>
        new(
            ForegroundChannelId,
            title,
            text,
            NotificationIconResourceName,
            AutoCancel: false,
            new[] { new NotificationActionSpec(StopAllActionId, stopLabel) });

    /// <summary>Builds the alert that explains a foreground-service timeout.</summary>
    /// <param name="title">Localized title.</param>
    /// <param name="text">Localized body.</param>
    public static NotificationContentSpec ForegroundTimeout(string title, string text) =>
        new(AlertChannelId, title, text, NotificationIconResourceName, AutoCancel: true, NoActions);

    /// <summary>Builds an incoming message notification.</summary>
    /// <param name="title">Localized title (the peer callsign).</param>
    /// <param name="text">Localized message body.</param>
    public static NotificationContentSpec Message(string title, string text) =>
        new(MessageChannelId, title, text, NotificationIconResourceName, AutoCancel: true, NoActions);
}

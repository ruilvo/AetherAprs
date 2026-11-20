// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Platform notification service. Core uses this to surface a notification for
/// an incoming APRS message; tapping it deep-links back into the conversation.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shows a notification for an incoming message.
    /// </summary>
    /// <param name="callsign">The sender callsign (displayed in the notification).</param>
    /// <param name="text">The message text.</param>
    /// <param name="conversationId">
    /// A stable identifier for the conversation the notification belongs to.
    /// Notifications for the same conversation overwrite each other.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    Task ShowMessageNotificationAsync(
        string callsign,
        string text,
        string conversationId,
        CancellationToken cancellationToken = default);
}

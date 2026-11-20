// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Safe default implementation of <see cref="INotificationService"/> for
/// platforms without notification support. All notifications are discarded.
/// </summary>
public sealed class NoOpNotificationService : INotificationService
{
    /// <inheritdoc/>
    public Task ShowMessageNotificationAsync(
        string callsign,
        string text,
        string conversationId,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

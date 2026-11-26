// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Configuration.Settings;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Configuration;

/// <summary>
/// Reads and updates the application settings.
/// </summary>
/// <remarks>
/// Settings are immutable snapshots: <see cref="Current"/> returns the
/// published instance and every change goes through <see cref="UpdateAsync"/>,
/// which persists the replacement before publishing it. Readers therefore never
/// see a partially applied change, and a caller cannot mutate state another
/// thread is reading.
/// </remarks>
public interface IConfigurationService
{
    /// <summary>
    /// Raised after a new snapshot has been persisted and published, so
    /// observers (map layers, packet lists) can refresh the values they derive
    /// from settings.
    /// </summary>
    event EventHandler? SettingsChanged;

    /// <summary>
    /// Gets the currently published settings snapshot. The instance never
    /// changes; retrieve it again after <see cref="SettingsChanged"/>.
    /// </summary>
    AppSettings Current { get; }

    /// <summary>
    /// Applies <paramref name="update"/> to the current snapshot, persists the
    /// result, and publishes it.
    /// </summary>
    /// <param name="update">
    /// Pure transformation that returns the replacement snapshot, typically a
    /// <c>with</c> expression. It runs inside the serialized update boundary, so
    /// it must be quick, must not block, and must not call back into this
    /// service.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait for the update boundary or the file write.</param>
    /// <returns>The published snapshot, or the unchanged current snapshot when the file is read-only.</returns>
    Task<AppSettings> UpdateAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken cancellationToken = default);
}

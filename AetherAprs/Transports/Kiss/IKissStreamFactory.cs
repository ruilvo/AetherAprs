// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration;

namespace AetherAprs.Transports.Kiss;

/// <summary>
/// Resolves and opens KISS transport streams from <see cref="KissSettings"/>.
/// </summary>
public interface IKissStreamFactory
{
    /// <summary>
    /// Settings types that have a supported connector registered.
    /// </summary>
    IReadOnlyCollection<Type> SupportedTransports { get; }

    /// <summary>
    /// Whether <paramref name="settingsType"/> has a connector that can actually
    /// connect on this platform.
    /// </summary>
    /// <remarks>
    /// <see cref="SupportedTransports"/> drives which transports the port editor
    /// offers. A transport without a supported connector is deliberately absent, so
    /// no progress or status report can make it look selectable.
    /// </remarks>
    bool IsTransportSupported(Type settingsType) => SupportedTransports.Contains(settingsType);

    /// <summary>
    /// Opens a duplex stream for the configured KISS transport.
    /// </summary>
    Task<Stream> OpenAsync(KissSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a duplex stream, reporting the stage the transport negotiation reaches.
    /// </summary>
    /// <param name="settings">The KISS settings naming the transport.</param>
    /// <param name="context">Per-attempt progress context.</param>
    /// <param name="cancellationToken">Cancels the connect.</param>
    /// <returns>The open duplex stream.</returns>
    /// <remarks>
    /// The default implementation ignores the context, so a test factory only has to
    /// supply a stream.
    /// </remarks>
    Task<Stream> OpenAsync(
        KissSettings settings,
        KissConnectionContext context,
        CancellationToken cancellationToken = default) => OpenAsync(settings, cancellationToken);
}

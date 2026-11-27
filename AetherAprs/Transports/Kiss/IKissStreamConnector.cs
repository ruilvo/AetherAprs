// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Transports.Kiss;

/// <summary>
/// Opens a duplex byte stream for a specific KISS transport settings type.
/// </summary>
public interface IKissStreamConnector
{
    /// <summary>The transport family this connector serves.</summary>
    /// <remarks>
    /// Defaulted to <see cref="KissTransportKind.Unknown"/> so a bare test connector
    /// only has to open a stream; a real connector states its family so a failure can
    /// name the transport it belongs to.
    /// </remarks>
    KissTransportKind Transport => KissTransportKind.Unknown;

    Type SettingsType { get; }

    bool IsSupported { get; }

    /// <summary>Opens the stream without reporting progress.</summary>
    /// <param name="settings">Transport settings for this connector's type.</param>
    /// <param name="cancellationToken">Cancels the connect.</param>
    /// <returns>The open duplex stream.</returns>
    Task<Stream> ConnectAsync(IKissTransportSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the stream, reporting the stage the attempt reaches.
    /// </summary>
    /// <param name="settings">Transport settings for this connector's type.</param>
    /// <param name="context">
    /// Per-attempt progress context; pass <see cref="KissConnectionContext.None"/>
    /// when no caller is observing.
    /// </param>
    /// <param name="cancellationToken">Cancels the connect.</param>
    /// <returns>The open duplex stream.</returns>
    /// <remarks>
    /// The default implementation ignores the context, so a connector that reports
    /// nothing still works; a connector that reports overrides it.
    /// </remarks>
    Task<Stream> ConnectAsync(
        IKissTransportSettings settings,
        KissConnectionContext context,
        CancellationToken cancellationToken = default) => ConnectAsync(settings, cancellationToken);
}

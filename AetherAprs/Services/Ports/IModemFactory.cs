// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration;
using AetherAprs.Modems.Aprs;
using AetherAprs.Transports.Kiss;

namespace AetherAprs.Services.Ports;

/// <summary>
/// A modem built for one configured port, together with every transport resource
/// created for it.
/// </summary>
/// <remarks>
/// The factory composes the whole resource graph, so it also owns the shutdown
/// order. A caller therefore disposes exactly one object and never has to know
/// that, for example, a KISS session also keeps its own stream wrapper alive
///.
/// </remarks>
public interface IPortModem : IAprsModem, IAsyncDisposable
{
    /// <summary>
    /// Transport family used by this modem, for diagnostics.
    /// </summary>
    string TransportName { get; }
}

/// <summary>
/// Selects and constructs the modem for a port's configured transport.
/// </summary>
/// <remarks>
/// Transport selection lives here rather than in <see cref="PortService"/> so the
/// session owner deals only with <see cref="IPortModem"/> and every connector is
/// opened through one code path that receives the caller's cancellation token
///.
/// </remarks>
public interface IModemFactory
{
    /// <summary>
    /// Creates a modem for <paramref name="port"/> without starting it.
    /// </summary>
    /// <param name="port">Configured port whose type settings select the transport.</param>
    /// <param name="cancellationToken">Token forwarded to the transport connector.</param>
    /// <returns>A modem that owns every resource created for it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="port"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the port has no type settings.</exception>
    /// <exception cref="NotSupportedException">Thrown when the settings type has no modem.</exception>
    Task<IPortModem> CreateAsync(PortConfig port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a modem for <paramref name="port"/>, reporting the stage the
    /// transport negotiation reaches.
    /// </summary>
    /// <param name="port">Configured port whose type settings select the transport.</param>
    /// <param name="context">
    /// Per-attempt progress context; pass <see cref="KissConnectionContext.None"/>
    /// when nobody is observing.
    /// </param>
    /// <param name="cancellationToken">Token forwarded to the transport connector.</param>
    /// <returns>A modem that owns every resource created for it.</returns>
    /// <remarks>
    /// The default implementation ignores the context and delegates to the plain
    /// overload, so a fake factory stays a one-method type.
    /// </remarks>
    Task<IPortModem> CreateAsync(
        PortConfig port,
        KissConnectionContext context,
        CancellationToken cancellationToken = default) => CreateAsync(port, cancellationToken);
}

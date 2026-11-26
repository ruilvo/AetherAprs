// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Modems.Aprs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Ports;

/// <summary>
/// A modem built for one configured port, together with every transport resource
/// created for it.
/// </summary>
/// <remarks>
/// The factory composes the whole resource graph, so it also owns the shutdown
/// order. A caller therefore disposes exactly one object and never has to know
/// that, for example, a KISS session also keeps its own stream wrapper alive
/// (audit C-01).
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
/// (audit P-18).
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
}

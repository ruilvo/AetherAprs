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
/// Selects an <see cref="IKissStreamConnector"/> by transport kind and opens a stream.
/// When multiple connectors share a kind, the supported connector is preferred.
/// </summary>
public sealed class KissStreamFactory : IKissStreamFactory
{
    private readonly IReadOnlyDictionary<KissTransportKind, IKissStreamConnector> _connectorsByKind;

    public KissStreamFactory(IEnumerable<IKissStreamConnector> connectors)
    {
        ArgumentNullException.ThrowIfNull(connectors);

        _connectorsByKind = connectors
            .GroupBy(connector => connector.Kind)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(connector => connector.IsSupported).First());
    }

    /// <inheritdoc />
    public IReadOnlyCollection<KissTransportKind> SupportedTransports =>
        _connectorsByKind.Values
            .Where(connector => connector.IsSupported)
            .Select(connector => connector.Kind)
            .ToArray();

    /// <inheritdoc />
    public Task<Stream> OpenAsync(KissSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Transport is null)
        {
            throw new InvalidOperationException("KISS transport settings are required.");
        }

        ValidateTransportKindMatchesSettings(settings.TransportKind, settings.Transport);

        if (!_connectorsByKind.TryGetValue(settings.TransportKind, out var connector))
        {
            throw new NotSupportedException($"No KISS stream connector is registered for transport kind '{settings.TransportKind}'.");
        }

        return connector.ConnectAsync(settings.Transport, cancellationToken);
    }

    private static void ValidateTransportKindMatchesSettings(KissTransportKind kind, IKissTransportSettings transport)
    {
        var matches = kind switch
        {
            KissTransportKind.Tcp => transport is TcpKissTransportSettings,
            KissTransportKind.BluetoothClassic => transport is BluetoothClassicKissTransportSettings,
            KissTransportKind.BluetoothLe => transport is BluetoothLeKissTransportSettings,
            _ => false
        };

        if (!matches)
        {
            throw new InvalidOperationException(
                $"KISS transport kind '{kind}' does not match transport settings type '{transport.GetType().Name}'.");
        }
    }
}
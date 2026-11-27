// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Transports.Kiss;

/// <summary>
/// Selects an <see cref="IKissStreamConnector"/> by settings type and opens a stream.
/// When multiple connectors share a settings type, the supported connector is preferred.
/// </summary>
public sealed class KissStreamFactory : IKissStreamFactory
{
    private readonly IReadOnlyDictionary<Type, IKissStreamConnector> _connectorsBySettingsType;
    private readonly HashSet<Type> _supportedSettingsTypes;

    public KissStreamFactory(IEnumerable<IKissStreamConnector> connectors)
    {
        ArgumentNullException.ThrowIfNull(connectors);

        _connectorsBySettingsType = connectors
            .GroupBy(connector => connector.SettingsType)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(connector => connector.IsSupported).First());

        // SupportedTransports is a projection of this set, so the editor and the
        // factory can never disagree about which transports exist.
        _supportedSettingsTypes = [.. _connectorsBySettingsType.Values
            .Where(connector => connector.IsSupported)
            .Select(connector => connector.SettingsType)];
    }

    /// <inheritdoc />
    public IReadOnlyCollection<Type> SupportedTransports => [.. _supportedSettingsTypes];

    /// <inheritdoc />
    public bool IsTransportSupported(Type settingsType)
    {
        ArgumentNullException.ThrowIfNull(settingsType);

        return _supportedSettingsTypes.Contains(settingsType);
    }

    /// <inheritdoc />
    public Task<Stream> OpenAsync(KissSettings settings, CancellationToken cancellationToken = default) =>
        OpenAsync(settings, KissConnectionContext.None, cancellationToken);

    /// <inheritdoc />
    public Task<Stream> OpenAsync(
        KissSettings settings,
        KissConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Transport is null)
        {
            throw new InvalidOperationException("KISS transport settings are required.");
        }

        Type transportType = settings.Transport.GetType();
        if (!_connectorsBySettingsType.TryGetValue(transportType, out IKissStreamConnector? connector))
        {
            throw new NotSupportedException($"No KISS stream connector is registered for transport type '{transportType.Name}'.");
        }

        return connector.ConnectAsync(settings.Transport, context, cancellationToken);
    }
}

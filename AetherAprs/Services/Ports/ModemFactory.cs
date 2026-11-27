// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Configuration.Settings;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Modems.Afsk;
using AetherAprs.Modems.Aprs;
using AetherAprs.Modems.Kiss;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Location;
using AetherAprs.Services.Platform;
using AetherAprs.Transports.Kiss;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Builds the modem for a port from its configured transport settings.
/// </summary>
/// <remarks>
/// The factory is the only place that maps <see cref="IPortTypeSettings"/> onto a
/// concrete modem. It also performs the runtime-dependent work that must happen
/// immediately before connecting, such as resolving the APRS-IS filter against the
/// current position.
/// </remarks>
public sealed class ModemFactory(
    IKissStreamFactory kissStreamFactory,
    IConfigurationService configurationService,
    IAudioStreamService audioStreamService,
    IAudioPlaybackService audioPlaybackService,
    IPttService pttService,
    ILoggerFactory loggerFactory,
    ILocationTrackingService? locationTrackingService = null) : IModemFactory
{
    /// <inheritdoc />
    public async Task<IPortModem> CreateAsync(PortConfig port, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(port);

        return port.TypeSettings switch
        {
            AprsIsSettings aprsIs => CreateAprsIsModem(aprsIs),
            KissSettings kiss => await CreateKissModemAsync(kiss, cancellationToken).ConfigureAwait(false),
            AfskSettings afsk => CreateAfskModem(afsk),
            null => throw new InvalidOperationException(
                $"Port '{port.Name}' has no type settings, so no modem can be created."),
            var unsupported => throw new NotSupportedException(
                $"No modem is available for settings type '{unsupported.GetType().Name}' " +
                $"(port '{port.Name}').")
        };
    }

    private IPortModem CreateAprsIsModem(AprsIsSettings settings)
    {
        Callsign callsign = ResolveCallsign();

        // Resolve runtime-dependent filter directives (drop a no-op all-types t/,
        // and turn m/ into a fixed r/ range around the current position) just
        // before connecting, because the position moves between sessions.
        LocationData? location = locationTrackingService?.CurrentLocation;
        string filter = AprsIsFilter.NormalizeForConnection(
            settings.Filter,
            location?.Location.Latitude,
            location?.Location.Longitude);

        AprsIsModem modem = new(
            settings.Server,
            settings.ServerPort,
            callsign,
            settings.Passcode,
            filter,
            settings.Security,
            loggerFactory.CreateLogger<AprsIsModem>());

        return new OwnedPortModem(modem, "APRS-IS");
    }

    private async Task<IPortModem> CreateKissModemAsync(
        KissSettings settings,
        CancellationToken cancellationToken)
    {
        Stream stream = await kissStreamFactory
            .OpenAsync(settings, cancellationToken)
            .ConfigureAwait(false);

        KissModem kissModem;
        try
        {
            kissModem = new KissModem(stream, loggerFactory);
        }
        catch
        {
            // The factory created the stream, so it must not leak it when the
            // modem cannot be built on top of it.
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        AprsRfModem modem = new(kissModem, loggerFactory);

        // The KISS modem owns the stream and must be disposed after the APRS
        // wrapper has unsubscribed and stopped.
        return new OwnedPortModem(modem, "KISS", kissModem);
    }

    private IPortModem CreateAfskModem(AfskSettings settings)
    {
        AfskModem modem = new(
            audioStreamService,
            audioPlaybackService,
            pttService,
            settings,
            loggerFactory);

        return new OwnedPortModem(modem, "AFSK");
    }

    private Callsign ResolveCallsign()
    {
        // The settings type validates the callsign when it is set, so the value
        // here is already a well-formed station identity or the modem start fails
        // with the same validation error the operator sees elsewhere.
        AprsSettings aprs = configurationService.Current.Aprs;
        int ssid = (int)aprs.Ssid;
        return new Callsign(aprs.Callsign, ssid > 0 ? ssid : null);
    }
}

/// <summary>
/// Adapts a modem to <see cref="IPortModem"/> and disposes the transport resources
/// that were created alongside it.
/// </summary>
internal sealed class OwnedPortModem(
    IAprsModem modem,
    string transportName,
    IAsyncDisposable? ownedTransport = null) : IPortModem
{
    private bool _disposed;

    /// <inheritdoc />
    public string TransportName { get; } = transportName;

    /// <inheritdoc />
    public event EventHandler<AprsPacket>? PacketReceived
    {
        add => modem.PacketReceived += value;
        remove => modem.PacketReceived -= value;
    }

    /// <inheritdoc />
    public event EventHandler<Exception>? ReceiveError
    {
        add => modem.ReceiveError += value;
        remove => modem.ReceiveError -= value;
    }

    /// <inheritdoc />
    public void Start() => modem.Start();

    /// <inheritdoc />
    public Task StopAsync() => modem.StopAsync();

    /// <inheritdoc />
    public Task SendAsync(AprsPacket packet, CancellationToken cancellationToken = default) =>
        modem.SendAsync(packet, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (modem is IAsyncDisposable modemDisposable)
            {
                await modemDisposable.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            if (ownedTransport is not null)
            {
                await ownedTransport.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

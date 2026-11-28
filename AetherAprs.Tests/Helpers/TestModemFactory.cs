// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration.Settings;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Location;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Transports.Kiss;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AetherAprs.Tests.Helpers;

/// <summary>
/// Builds a <see cref="ModemFactory"/> wired with inert platform services, so a
/// port service test only overrides the transport it actually exercises.
/// </summary>
internal static class TestModemFactory
{
    public static ModemFactory Create(
        IKissStreamFactory? kissStreamFactory = null,
        IAudioStreamService? audioStreamService = null,
        IAudioPlaybackService? audioPlaybackService = null,
        IPttService? pttService = null,
        IConfigurationService? configurationService = null,
        ILoggerFactory? loggerFactory = null,
        ILocationTrackingService? locationTrackingService = null) =>
        new(
            kissStreamFactory ?? new KissStreamFactory([new TcpKissStreamConnector()]),
            configurationService ?? new StubConfigurationService(),
            audioStreamService ?? new NoOpAudioStreamService(),
            audioPlaybackService ?? new NoOpAudioPlaybackService(),
            pttService ?? new NoOpPttService(),
            loggerFactory ?? NullLoggerFactory.Instance,
            locationTrackingService);

    /// <summary>
    /// Configuration with default settings, so APRS-IS modem creation has a
    /// callsign without each test declaring one.
    /// </summary>
    private sealed class StubConfigurationService : IConfigurationService
    {
#pragma warning disable CS0067 // Event is required by the interface but unused.
        public event EventHandler? SettingsChanged;
#pragma warning restore CS0067

        public AppSettings Current { get; set; } = new();

        public Task<AppSettings> UpdateAsync(
            Func<AppSettings, AppSettings> update,
            CancellationToken cancellationToken = default)
        {
            Current = update(Current);
            return Task.FromResult(Current);
        }
    }
}

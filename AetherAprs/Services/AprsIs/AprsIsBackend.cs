// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.AprsIs;

using AetherAprs.Models;
using AetherAprs.Protocols;
using AetherAprs.Protocols.AprsIs;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class AprsIsBackend : IAprsBackend, IAsyncDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly ILoggingService _log;
    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public AprsIsBackend(IConfigurationService configurationService, ILoggingService loggingService)
    {
        _configurationService = configurationService;
        _log = loggingService.ForContext(nameof(AprsIsBackend));
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_tcpClient is not null)
        {
            _log.Debug("ConnectAsync called while already connected; no-op.");
            return;
        }

        var settings = _configurationService.Settings;
        _log.Info($"Connecting to {settings.AprsIs.Host}:{settings.AprsIs.Port} as {settings.AprsIs.Callsign}.");

        try
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(settings.AprsIs.Host, settings.AprsIs.Port, cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Error($"TCP connect to {settings.AprsIs.Host}:{settings.AprsIs.Port} failed: {ex.Message}.");
            _tcpClient?.Dispose();
            _tcpClient = null;
            throw;
        }

        var stream = _tcpClient.GetStream();
        _reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        _writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n",
            AutoFlush = true,
        };

        var login = $"user {settings.AprsIs.Callsign} pass {settings.AprsIs.Passcode} vers AetherAprs 0.1";
        _log.Debug($"Sending login line for {settings.AprsIs.Callsign}.");
        await _writer.WriteLineAsync(login);

        if (!string.IsNullOrWhiteSpace(settings.AprsIs.Filter))
        {
            _log.Debug($"Sending filter: {settings.AprsIs.Filter}.");
            await _writer.WriteLineAsync($"#filter {settings.AprsIs.Filter}");
        }

        _log.Info("APRS-IS connection established.");
    }

    public async IAsyncEnumerable<AprsPacket> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_reader is null)
        {
            _log.Error("ReceiveAsync called before ConnectAsync.");
            throw new InvalidOperationException("APRS-IS backend is not connected.");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                _log.Info("APRS-IS stream closed by remote.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                _log.Debug($"Server comment: {line}.");
                continue;
            }

            AprsIsPacket packet;
            try
            {
                packet = AprsIsCodec.Decode(line);
            }
            catch (InvalidOperationException ex)
            {
                _log.Warn($"Failed to decode APRS-IS line ({ex.Message}): {Truncate(line, 200)}.");
                continue;
            }

            yield return packet.Packet;
        }

        _log.Debug("ReceiveAsync exiting due to cancellation.");
    }

    public async ValueTask SendAsync(AprsPacket packet, CancellationToken cancellationToken = default)
    {
        if (_writer is null)
        {
            _log.Error("SendAsync called before ConnectAsync.");
            throw new InvalidOperationException("APRS-IS backend is not connected.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var line = AprsIsCodec.Encode(new AprsIsPacket { Packet = packet });
        _log.Debug($"TX: {Truncate(line, 200)}.");
        await _writer.WriteLineAsync(line);
    }

    public ValueTask DisposeAsync()
    {
        if (_tcpClient is not null)
        {
            _log.Info("Disposing APRS-IS connection.");
        }

        _reader?.Dispose();
        _writer?.Dispose();
        _tcpClient?.Dispose();
        _reader = null;
        _writer = null;
        _tcpClient = null;
        return ValueTask.CompletedTask;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}

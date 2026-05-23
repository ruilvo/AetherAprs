// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.AprsIs;

using AetherAprs.Models;
using AetherAprs.Protocols;
using AetherAprs.Protocols.AprsIs;
using AetherAprs.Services.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class AprsIsBackend(IConfigurationService configurationService) : IAprsBackend, IAsyncDisposable
{
    private const string DefaultHost = "rotate.aprs2.net";
    private const int DefaultPort = 14580;

    private readonly IConfigurationService _configurationService = configurationService;
    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_tcpClient is not null)
        {
            return;
        }

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(DefaultHost, DefaultPort, cancellationToken);
        var stream = _tcpClient.GetStream();
        _reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        _writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n",
            AutoFlush = true,
        };

        var settings = _configurationService.Settings;
        var login = $"user {settings.Callsign} pass {settings.Passcode} vers AetherAprs 0.1";
        await _writer.WriteLineAsync(login);

        if (!string.IsNullOrWhiteSpace(settings.Filter))
        {
            await _writer.WriteLineAsync($"#filter {settings.Filter}");
        }
    }

    public async IAsyncEnumerable<AprsPacket> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_reader is null)
        {
            throw new InvalidOperationException("APRS-IS backend is not connected.");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            AprsIsPacket packet;
            try
            {
                packet = AprsIsCodec.Decode(line);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            yield return packet.Packet;
        }
    }

    public async ValueTask SendAsync(AprsPacket packet, CancellationToken cancellationToken = default)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("APRS-IS backend is not connected.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var line = AprsIsCodec.Encode(new AprsIsPacket { Packet = packet });
        await _writer.WriteLineAsync(line);
    }

    public ValueTask DisposeAsync()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _tcpClient?.Dispose();
        _reader = null;
        _writer = null;
        _tcpClient = null;
        return ValueTask.CompletedTask;
    }
}

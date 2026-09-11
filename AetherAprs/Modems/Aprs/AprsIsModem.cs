// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Models.Aprs;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Modems.Aprs;

/// <summary>
/// APRS-IS modem that communicates with an APRS-IS server over TCP.
///
/// Connects to an APRS-IS server, authenticates, and sends/receives
/// APRS packets as text lines in the format <c>source&gt;dest:info</c>.
/// </summary>
public sealed class AprsIsModem : IAprsModem, IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly Callsign _callsign;
    private readonly string _passcode;
    private readonly string _filter;
    private readonly ILogger<AprsIsModem> _logger;
    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Stream? _stream;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private TaskCompletionSource<bool> _connectionReady = CreateConnectionReadySource();
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="AprsIsModem"/>.
    /// </summary>
    /// <param name="host">The APRS-IS server hostname or IP address.</param>
    /// <param name="port">The APRS-IS server TCP port (typically 14580 for receive+send, 14501 for send-only).</param>
    /// <param name="callsign">The callsign to authenticate with.</param>
    /// <param name="passcode">The APRS-IS passcode for the callsign.</param>
    /// <param name="filter">Optional APRS-IS filter string (e.g. <c>m/50</c> for messages only). Leave empty for all.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="host"/> or <paramref name="callsign"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="host"/> is empty or whitespace, or <paramref name="passcode"/> is null.</exception>
    public AprsIsModem(string host, int port, Callsign callsign, string passcode, string filter = "")
        : this(host, port, callsign, passcode, filter, new NoOpLogger<AprsIsModem>())
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AprsIsModem"/>.
    /// </summary>
    /// <param name="host">The APRS-IS server hostname or IP address.</param>
    /// <param name="port">The APRS-IS server TCP port (typically 14580 for receive+send, 14501 for send-only).</param>
    /// <param name="callsign">The callsign to authenticate with.</param>
    /// <param name="passcode">The APRS-IS passcode for the callsign.</param>
    /// <param name="filter">Optional APRS-IS filter string (e.g. <c>m/50</c> for messages only). Leave empty for all.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="host"/> or <paramref name="callsign"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="host"/> is empty or whitespace, or <paramref name="passcode"/> is null.</exception>
    public AprsIsModem(string host, int port, Callsign callsign, string passcode, string filter, ILogger<AprsIsModem> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrEmpty(passcode);

        _host = host;
        _port = port;
        _callsign = callsign;
        _passcode = passcode;
        _filter = filter ?? string.Empty;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<AprsPacket>? PacketReceived;

    /// <inheritdoc />
    public event EventHandler<Exception>? ReceiveError;

    /// <inheritdoc />
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_started)
        {
            throw new InvalidOperationException("AprsIsModem is already started.");
        }

        _connectionReady = CreateConnectionReadySource();
        _readCts = new CancellationTokenSource();
        _readTask = ReadLoopAsync(_readCts.Token);

        _started = true;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (!_started)
        {
            return;
        }

        if (_readCts is not null)
        {
            await _readCts.CancelAsync();
        }

        _connectionReady.TrySetCanceled();

        if (_readTask is not null)
        {
            try
            {
                await _readTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation.
            }
        }

        _started = false;
    }

    /// <inheritdoc />
    public async Task SendAsync(AprsPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (!_started)
        {
            throw new InvalidOperationException("Modem is not connected. Call Start() first and ensure connection is established.");
        }

        await _connectionReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        // APRS-IS line format: source>dest:info_field
        var infoField = AprsSerializer.FormatInfoField(packet);
        if (_writer is null)
        {
            throw new InvalidOperationException("Modem is not connected. Call Start() first and ensure connection is established.");
        }

        var line = $"{packet.Source}>{packet.Destination}:{infoField}";
        await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _writer.FlushAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await StopAsync();

        _reader?.Dispose();
        _writer?.Dispose();
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _readCts?.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        // Keep reconnecting as long as we haven't been cancelled.
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ReceiveError?.Invoke(this, ex);

                // Wait before reconnecting
                try
                {
                    await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ConnectAndReadAsync(CancellationToken cancellationToken)
    {
        _tcpClient?.Dispose();
        _reader?.Dispose();
        _writer?.Dispose();
        _stream?.Dispose();

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(_host, _port, cancellationToken).ConfigureAwait(false);

        _stream = _tcpClient.GetStream();
        _reader = new StreamReader(_stream, Encoding.ASCII, leaveOpen: true);
        _writer = new StreamWriter(_stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n"
        };

        // Send APRS-IS login
        var login = $"user {_callsign.Base} pass {_passcode} vers AetherAprs 1.0";
        if (!string.IsNullOrEmpty(_filter))
        {
            login += $" filter {_filter}";
        }

        _logger.LogInformation(
            "Connecting to APRS-IS {Host}:{Port} with filter {Filter}",
            _host,
            _port,
            string.IsNullOrEmpty(_filter) ? "<none>" : _filter);

        await _writer.WriteLineAsync(login.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _writer.FlushAsync().ConfigureAwait(false);
        _connectionReady.TrySetResult(true);

        // Read lines from the server
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line is null)
             {
                 // Connection closed by server
                 _logger.LogInformation("Connection closed by server.");
                 break;
             }

             _logger.LogInformation("Server response: {Response}", line);

             if (line.Length == 0 || line[0] == '#')
             {
                 // Empty or comment line (server status messages start with #)
                 continue;
             }

             try
             {
                 var packet = ParseIsLine(line);
                 if (packet is not null)
                 {
                     PacketReceived?.Invoke(this, packet);
                 }
             }
             catch (Exception ex)
             {
                 ReceiveError?.Invoke(this, ex);
             }
         }
    }

    private static TaskCompletionSource<bool> CreateConnectionReadySource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Parses an APRS-IS text line into an <see cref="AprsPacket"/>.
    /// Format: <c>source&gt;dest[,digi1,digi2,...]:info_field</c>
    /// </summary>
    private static AprsPacket? ParseIsLine(string line)
    {
        // Find the colon that separates the path from the info field
        int colonIndex = line.IndexOf(':');
        if (colonIndex < 0)
        {
            return null;
        }

        string path = line[..colonIndex];
        string info = line[(colonIndex + 1)..];

        // Find '>' to split source from destination
        int arrowIndex = path.IndexOf('>');
        if (arrowIndex < 0)
        {
            return null;
        }

        string sourceStr = path[..arrowIndex];
        string destStr = path[(arrowIndex + 1)..];

        // Trim any digipeaters from destination (after first comma)
        int commaIndex = destStr.IndexOf(',');
        if (commaIndex >= 0)
        {
            destStr = destStr[..commaIndex];
        }

        Callsign source;
        Callsign destination;
        string? rawSource = null;

        try
        {
            source = ParseCallsign(sourceStr);
            destination = ParseCallsign(destStr);
        }
        catch (ArgumentException)
        {
            if (!IsExtendedSourceIdentifier(sourceStr))
            {
                return null;
            }

            source = new Callsign("APRS");
            rawSource = sourceStr.Trim().ToUpperInvariant();

            try
            {
                destination = ParseCallsign(destStr);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        var packet = AprsParser.ParseInfoField(info, source, destination);
        return packet with { RawSource = rawSource };
    }

    private static bool IsExtendedSourceIdentifier(string value)
    {
        string identifier = value.Trim();
        int dashIndex = identifier.LastIndexOf('-');

        if (dashIndex > 0)
        {
            if (dashIndex == identifier.Length - 1 ||
                !int.TryParse(identifier[(dashIndex + 1)..], out var ssid) ||
                ssid is < 0 or > 15)
            {
                return false;
            }

            identifier = identifier[..dashIndex];
        }

        return identifier.Length is > 6 and <= 9 &&
               identifier.All(char.IsAsciiLetterOrDigit);
    }

    /// <summary>
    /// Parses a callsign string that may include an SSID suffix (e.g. "N0CALL-1").
    /// </summary>
    private static Callsign ParseCallsign(string value)
    {
        int dashIndex = value.LastIndexOf('-');
        if (dashIndex > 0 && dashIndex < value.Length - 1)
        {
            string baseCall = value[..dashIndex];
            if (int.TryParse(value[(dashIndex + 1)..], out int ssid))
            {
                return new Callsign(baseCall, ssid);
            }
        }

        return new Callsign(value, null);
    }

    /// <summary>
    /// A no-operation logger implementation used as a default when no logger is provided.
    /// </summary>
    private sealed class NoOpLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // No-op
        }
    }
}

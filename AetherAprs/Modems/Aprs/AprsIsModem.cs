// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
    private readonly Lock _connectionLock = new();

    /// <summary>Serializes concurrent sends so APRS-IS lines cannot interleave on the socket.</summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    /// <summary>Serializes Start/Stop/Dispose state transitions so concurrent calls cannot double-start the read loop.</summary>
    private readonly Lock _lifecycleLock = new();
    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Stream? _stream;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private TaskCompletionSource<bool> _connectionReady = CreateConnectionReadySource();
    private int _connectionVersion;
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
    /// <summary>
    /// Maximum length of an APRS-IS line we accept before discarding it as hostile
    /// or malformed input.
    /// </summary>
    private const int MaxLineLength = 512;

    /// <summary>
    /// Software version string reported during APRS-IS login.
    /// </summary>
    private const string ApplicationVersion = "AetherAprs 1.0";

    public AprsIsModem(string host, int port, Callsign callsign, string passcode, string filter = "")
        : this(host, port, callsign, passcode, filter, NullLogger<AprsIsModem>.Instance)
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

        lock (_lifecycleLock)
        {
            if (_started)
            {
                throw new InvalidOperationException("AprsIsModem is already started.");
            }

            _connectionReady = CreateConnectionReadySource();
            _readCts = new CancellationTokenSource();
            _readTask = ReadLoopAsync(_readCts.Token);

            _started = true;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        CancellationTokenSource? readCts;
        Task? readTask;

        lock (_lifecycleLock)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            readCts = _readCts;
            readTask = _readTask;
            _readCts = null;
            _readTask = null;
        }

        if (readCts is not null)
        {
            await readCts.CancelAsync();
            readCts.Dispose();
        }

        _connectionReady.TrySetCanceled();

        if (readTask is not null)
        {
            try
            {
                await readTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation.
            }
        }

        // Close the connection eagerly so stop-and-restart does not leave a
        // lingering socket until DisposeAsync.
        Disconnect();
    }

    /// <summary>
    /// Disposes the current TCP resources under the connection lock.
    /// </summary>
    private void Disconnect()
    {
        lock (_connectionLock)
        {
            _tcpClient?.Dispose();
            _reader?.Dispose();
            _writer?.Dispose();
            _stream?.Dispose();

            _tcpClient = null;
            _reader = null;
            _writer = null;
            _stream = null;
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(AprsPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (!_started)
        {
            throw new InvalidOperationException("Modem is not connected. Call Start() first and ensure connection is established.");
        }

        // APRS-IS client-originated packets must identify the TCP connection in the path.
        var infoField = AprsInfoFieldSerializer.FormatInfoField(packet);

        // Capture writer, ready task and connection version atomically under lock so
        // a concurrent reconnect cannot leave us awaiting a stale completion source.
        StreamWriter? writer;
        Task readyTask;
        int connectionVersion;
        lock (_connectionLock)
        {
            writer = _writer;
            readyTask = _connectionReady.Task;
            connectionVersion = _connectionVersion;
        }

        await readyTask.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (writer is null)
        {
            throw new InvalidOperationException("Modem is not connected. Call Start() first and ensure connection is established.");
        }

        try
        {
            var line = $"{packet.Source}>{packet.Destination},TCPIP*:{infoField}";

            // Sanitize line breaks to prevent protocol injection
            if (line.Contains('\r') || line.Contains('\n'))
            {
                line = line.Replace("\r", "").Replace("\n", "");
                _logger.LogWarning(
                    "Removed line break characters from outbound APRS-IS packet (potential protocol injection attempt).");
            }

            // The StreamWriter is not thread-safe: serialize writes so two
            // concurrent sends cannot interleave buffered writes/flushes.
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (Exception) when (!IsCurrentConnection(connectionVersion))
        {
            // Connection changed while we were sending - throw a more specific exception
            throw new InvalidOperationException("Connection was reset during send operation. Retry with a new connection.");
        }
    }

    private bool IsCurrentConnection(int expectedVersion)
    {
        lock (_connectionLock)
        {
            return _connectionVersion == expectedVersion;
        }
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

        _sendLock.Dispose();
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
        var connectionReady = CreateConnectionReadySource();
        lock (_connectionLock)
        {
            _connectionReady = connectionReady;
        }

        // Increment connection version and dispose old resources under lock
        Disconnect();
        lock (_connectionLock)
        {
            _connectionVersion++;
        }

        var tcpClient = new TcpClient();
        StreamReader? reader = null;
        StreamWriter? writer = null;
        try
        {
            await tcpClient.ConnectAsync(_host, _port, cancellationToken).ConfigureAwait(false);

            var stream = tcpClient.GetStream();
            reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
            {
                NewLine = "\r\n"
            };

            // Send APRS-IS login
            var login = $"user {_callsign} pass {_passcode} vers {ApplicationVersion}";
            if (!string.IsNullOrEmpty(_filter))
            {
                login += $" filter {_filter}";
            }

            // Sanitize line breaks to prevent protocol injection
            if (login.Contains('\r') || login.Contains('\n'))
            {
                login = login.Replace("\r", "").Replace("\n", "");
                _logger.LogWarning(
                    "Removed line break characters from APRS-IS login string (potential protocol injection attempt).");
            }

            _logger.LogInformation(
                "Connecting to APRS-IS {Host}:{Port} with filter {Filter}",
                _host,
                _port,
                string.IsNullOrEmpty(_filter) ? "<none>" : _filter);

            await writer.WriteLineAsync(login.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Assign to fields under lock only after successful connection
            lock (_connectionLock)
            {
                _tcpClient = tcpClient;
                _stream = stream;
                _reader = reader;
                _writer = writer;
            }
        }
        catch
        {
            // Dispose the locals we own — the field-level cleanup only sees fields
            // that were assigned after a successful login, so without this every
            // failed attempt leaks a socket.
            reader?.Dispose();
            writer?.Dispose();
            tcpClient.Dispose();
            throw;
        }

        // Read lines from the server
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = null;
            try
            {
                line = await ReadBoundedLineAsync(reader, MaxLineLength, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                // Line exceeded MaxLineLength
                _logger.LogWarning(ex, "Discarding oversized APRS-IS line from {Host}:{Port}.", _host, _port);
                continue;
            }

            if (line is null)
            {
                // Connection closed by server
                _logger.LogInformation("Connection closed by server.");
                break;
            }

            _logger.LogInformation("Server response: {Response}", line);

            if (line.StartsWith("# logresp ", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains(" verified", StringComparison.OrdinalIgnoreCase))
                {
                    connectionReady.TrySetResult(true);
                }
                else
                {
                    FaultConnectionReady(connectionReady, new InvalidOperationException($"APRS-IS login was rejected: {line}"));
                }
            }

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

        FaultConnectionReady(
            connectionReady,
            new IOException("APRS-IS connection closed before authentication completed."));
    }

    /// <summary>
    /// Faults the connection-ready source while ensuring the fault is observed, so
    /// it does not surface as an unobserved task exception at garbage collection.
    /// </summary>
    private static void FaultConnectionReady(TaskCompletionSource<bool> connectionReady, Exception exception)
    {
        if (!connectionReady.TrySetException(exception))
        {
            return;
        }

        // No-op observer for the faulted task.
        _ = connectionReady.Task.ContinueWith(
            static t => _ = t.Exception,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    private static TaskCompletionSource<bool> CreateConnectionReadySource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Parses an APRS-IS text line into an <see cref="AprsPacket"/>.
    /// Format: <c>source&gt;dest[,digi1,digi2,...]:info_field</c>
    /// </summary>
    internal static AprsPacket? ParseIsLine(string line)
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
        string pathTail = path[(arrowIndex + 1)..];

        // Split the destination from the digipeater hops
        int commaIndex = pathTail.IndexOf(',');
        string destStr = commaIndex >= 0 ? pathTail[..commaIndex] : pathTail;

        Callsign source;
        Callsign destination;
        string? rawSource = null;

        if (IsExtendedSourceIdentifier(sourceStr))
        {
            source = new Callsign("APRS");
            rawSource = sourceStr.Trim().ToUpperInvariant();
        }
        else
        {
            try
            {
                source = ParseCallsign(sourceStr);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        try
        {
            destination = ParseCallsign(destStr);
        }
        catch (ArgumentException)
        {
            return null;
        }

        // Parse the digipeater hops so IS packets keep their path (trails/traces
        // downstream rely on it). Network-injected entries and server markers are
        // skipped; malformed hops are ignored rather than dropping the packet.
        List<Callsign> pathList = [];
        if (commaIndex >= 0)
        {
            foreach (var hop in pathTail[(commaIndex + 1)..].Split(','))
            {
                if (hop.Length == 0 || hop == "TCPIP*")
                {
                    continue;
                }

                // Server-inserted q-constructors (qAC, qAR, qAo, qAS, qX, ...) are
                // APRS-IS markers, not real digipeater hops — skip them all.
                if (hop.Length == 3 && hop[0] == 'q' && char.IsAsciiLetterUpper(hop[1]))
                {
                    continue;
                }

                try
                {
                    pathList.Add(ParseCallsign(hop));
                }
                catch (ArgumentException)
                {
                    // Skip unparseable hops.
                }
            }
        }

        var packet = AprsInfoFieldParser.ParseInfoField(info, source, destination, pathList);
        return packet with { RawSource = rawSource };
    }

    private static bool IsExtendedSourceIdentifier(string value)
    {
        string identifier = value.Trim();
        int dashIndex = identifier.LastIndexOf('-');

        if (dashIndex <= 0)
        {
            return identifier.Length > 6 && identifier.Length <= 9 &&
                   identifier.All(char.IsAsciiLetterOrDigit);
        }

        string baseCall = identifier[..dashIndex];
        string ssid = identifier[(dashIndex + 1)..];
        if (baseCall.Length is < 2 or > 6 ||
            !baseCall.All(char.IsAsciiLetterOrDigit) ||
            ssid.Length is < 1 or > 2 ||
            !ssid.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        return !int.TryParse(ssid, out var numericSsid) || numericSsid is < 0 or > 15;
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
                // Throwing ArgumentException (not ArgumentOutOfRangeException) keeps
                // unparseable hops inside the catch guard that skips them.
                if (ssid is < 0 or > 15)
                {
                    throw new ArgumentException($"SSID must be between 0 and 15: '{value}'.", nameof(value));
                }

                return new Callsign(baseCall, ssid);
            }
        }

        return new Callsign(value, null);
    }

    /// <summary>
    /// Reads a line from the stream with a maximum length limit to prevent unbounded memory allocation.
    /// </summary>
    /// <param name="reader">The StreamReader to read from.</param>
    /// <param name="maxLength">Maximum allowed line length in characters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The line read from the stream, or null if end of stream is reached.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a line exceeds maxLength.</exception>
    private static async Task<string?> ReadBoundedLineAsync(
        StreamReader reader,
        int maxLength,
        CancellationToken cancellationToken)
    {
        var buffer = new char[maxLength + 1]; // +1 to detect overflow
        int position = 0;

        while (true)
        {
            int c = await reader.ReadAsync(buffer.AsMemory(position, 1), cancellationToken).ConfigureAwait(false);
            
            if (c == 0)
            {
                // End of stream
                return position == 0 ? null : new string(buffer, 0, position);
            }

            char ch = buffer[position];

            if (ch == '\n')
            {
                // End of line found
                // Remove trailing \r if present (handles both \r\n and \n)
                int length = position;
                if (length > 0 && buffer[length - 1] == '\r')
                {
                    length--;
                }
                return new string(buffer, 0, length);
            }

            position++;

            if (position > maxLength)
            {
                // Line exceeds maximum length - discard the rest until \n
                while (true)
                {
                    int next = reader.Read();
                    if (next == -1 || next == '\n')
                    {
                        break;
                    }
                }
                throw new InvalidOperationException($"Line exceeded maximum length of {maxLength} characters.");
            }
        }
    }
}

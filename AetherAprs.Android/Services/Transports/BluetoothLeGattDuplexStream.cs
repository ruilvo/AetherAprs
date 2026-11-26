// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Android.Bluetooth;
using AetherAprs.Services.Bluetooth;
using Java.Util;
using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services.Transports;

/// <summary>
/// Duplex stream backed by a connected BLE GATT Nordic-UART-style pair of characteristics.
/// Read pulls from TX notifications; write pushes to the RX characteristic.
/// </summary>
internal sealed class BluetoothLeGattDuplexStream : Stream
{
    private static readonly UUID CccdUuid = UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;

    /// <summary>Requested ATT MTU. 517 is the largest ATT PDU size a peer can accept.</summary>
    private const int RequestedAttMtu = 517;

    /// <summary>ATT MTU used when negotiation is unavailable or fails.</summary>
    private const int DefaultAttMtu = 23;

    /// <summary>ATT write header (opcode + handle) that does not fit in the payload.</summary>
    private const int AttHeaderSize = 3;

    /// <summary>Usable write payload with the requested MTU.</summary>
    private const int MaxPayloadSize = RequestedAttMtu - AttHeaderSize;

    /// <summary>Usable write payload with the default MTU.</summary>
    private const int DefaultPayloadSize = DefaultAttMtu - AttHeaderSize;

    /// <summary>ERROR_GATT_WRITE_REQUEST_BUSY: another ATT request is in flight.</summary>
    private const int GattWriteRequestBusyStatus = 129;

    /// <summary>GATT_ERROR (133): transient stack failure on many devices.</summary>
    private const int GattConnectionErrorStatus = 133;

    /// <summary>Bounded wait for the peer/stack to answer the MTU request.</summary>
    private static readonly TimeSpan MtuNegotiationTimeout = TimeSpan.FromSeconds(2);

    private readonly BluetoothGatt _gatt;
    private readonly BluetoothGattCharacteristic _rxCharacteristic;
    private readonly Channel<byte> _rxChannel;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _sync = new();
    private readonly BluetoothLeFrameWriter _frameWriter;

    private TaskCompletionSource<GattStatus>? _writeCompletion;
    private bool _writeAbandoned;
    private bool _disposed;

    /// <summary>Usable bytes per WriteCharacteristic call, derived from the negotiated MTU.</summary>
    private int _payloadSize = DefaultPayloadSize;

    /// <summary>Whether the MTU was reported (or assumed) for this connection.</summary>
    private bool _mtuReported;

    private BluetoothLeGattDuplexStream(
        BluetoothGatt gatt,
        BluetoothGattCharacteristic rxCharacteristic,
        Channel<byte> rxChannel)
    {
        _gatt = gatt;
        _rxCharacteristic = rxCharacteristic;
        _rxChannel = rxChannel;
        _frameWriter = new BluetoothLeFrameWriter(new GattChunkWriter(this));
    }

    public override bool CanRead => !_disposed;

    public override bool CanSeek => false;

    public override bool CanWrite => !_disposed;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    private static BluetoothGatt? StartGattConnection(
        BluetoothDevice device,
        global::Android.Content.Context context,
        BluetoothGattCallback callback)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(37))
        {
            var settings = new BluetoothGattConnectionSettings.Builder()
                .SetAutoConnectEnabled(false)
                .SetTransport((int)BluetoothTransports.Le)
                .Build();

            if (settings is null)
            {
                throw new InvalidOperationException("The BLE connection settings could not be built.");
            }

            var executor = context.MainExecutor
                ?? throw new InvalidOperationException("The Android context has no main executor.");

            return device.ConnectGatt(settings, executor, callback);
        }

        // Android 17 (API 37) obsoleted every context-based overload in favour of
        // the settings/executor form, but that form does not exist below 37 and the
        // application still supports API 33.
#pragma warning disable CA1422 // The executor overload is unavailable below API 37.
        return device.ConnectGatt(context, autoConnect: false, callback, BluetoothTransports.Le);
#pragma warning restore CA1422
    }

    public static async Task<BluetoothLeGattDuplexStream> ConnectAsync(
        BluetoothDevice device,
        Guid serviceUuid,
        Guid rxCharacteristicUuid,
        Guid txCharacteristicUuid,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        var context = global::Android.App.Application.Context;
        var rxChannel = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        var readyTcs = new TaskCompletionSource<BluetoothLeGattDuplexStream>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new GattCallback(
            serviceUuid,
            rxCharacteristicUuid,
            txCharacteristicUuid,
            rxChannel,
            readyTcs);
        var gatt = StartGattConnection(device, context, callback)
            ?? throw new InvalidOperationException("Failed to start BLE GATT connection.");
        await using (cancellationToken.Register(() =>
        {
            readyTcs.TrySetCanceled(cancellationToken);
            try
            {
                gatt.Disconnect();
                gatt.Close();
            }
            catch
            {
                // Best-effort cancel.
            }
        }))
        {
            try
            {
                return await readyTcs.Task.ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    gatt.Disconnect();
                    gatt.Close();
                }
                catch
                {
                    // Ignore cleanup failures while propagating connect error.
                }

                gatt.Dispose();
                throw;
            }
        }
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.Length == 0)
        {
            return 0;
        }

        var written = 0;

        while (written < buffer.Length)
        {
            byte next;
            try
            {
                if (written == 0)
                {
                    next = await _rxChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (!_rxChannel.Reader.TryRead(out next))
                {
                    break;
                }
            }
            catch (ChannelClosedException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
            catch (ChannelClosedException)
            {
                break;
            }

            buffer.Span[written++] = next;

            while (written < buffer.Length && _rxChannel.Reader.TryRead(out next))
            {
                buffer.Span[written++] = next;
            }

            if (written > 0)
            {
                break;
            }
        }

        return written;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.IsEmpty)
        {
            return;
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var abandoned = false;
        try
        {
            await _frameWriter.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A write-with-response whose callback has not arrived keeps the write
            // gate held, so the next write cannot start before the stale callback is
            // consumed (CompleteWrite releases the gate in that case).
            lock (_sync)
            {
                abandoned = _writeAbandoned;
            }

            throw;
        }
        finally
        {
            if (!abandoned)
            {
                _writeGate.Release();
            }
        }
    }

    /// <summary>
    /// Attempts one payload-sized chunk write. Write-with-response uses the single
    /// in-flight <see cref="_writeCompletion"/> protocol; slicing, pacing and
    /// retries are decided by <see cref="BluetoothLeFrameWriter"/>.
    /// </summary>
    private async Task<GattWriteOutcome> WriteChunkOnceAsync(
        ReadOnlyMemory<byte> chunk,
        bool withoutResponse,
        CancellationToken cancellationToken)
    {
        var writeType = withoutResponse ? GattWriteType.NoResponse : GattWriteType.Default;

        if (writeType == GattWriteType.Default)
        {
            var writeTcs = new TaskCompletionSource<GattStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync)
            {
                _writeCompletion = writeTcs;
            }

            var startStatus = _gatt.WriteCharacteristic(_rxCharacteristic, chunk.ToArray(), (int)writeType);
            if (startStatus != (int)GattStatus.Success)
            {
                lock (_sync)
                {
                    if (_writeCompletion == writeTcs)
                    {
                        _writeCompletion = null;
                    }
                }

                return ToOutcome(startStatus);
            }

            GattStatus completedStatus;
            try
            {
                completedStatus = await writeTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    if (_writeCompletion == writeTcs)
                    {
                        _writeAbandoned = true;
                    }
                }

                throw;
            }

            return ToOutcome((int)completedStatus);
        }

        return ToOutcome(_gatt.WriteCharacteristic(_rxCharacteristic, chunk.ToArray(), (int)writeType));
    }

    private static GattWriteOutcome ToOutcome(int status) => status switch
    {
        0 => GattWriteOutcome.Success,
        GattWriteRequestBusyStatus => GattWriteOutcome.Busy,
        GattConnectionErrorStatus => GattWriteOutcome.ConnectionError,
        _ => GattWriteOutcome.Rejected
    };

    /// <summary>Exposes the connected characteristic to the frame writer.</summary>
    private sealed class GattChunkWriter(BluetoothLeGattDuplexStream stream) : IBluetoothLeChunkWriter
    {
        public bool SupportsWriteWithoutResponse =>
            (stream._rxCharacteristic.Properties & GattProperty.WriteNoResponse) != 0;

        public int PayloadSize => stream.PayloadSize;

        public Task<GattWriteOutcome> WriteChunkAsync(
            ReadOnlyMemory<byte> chunk,
            bool withoutResponse,
            CancellationToken cancellationToken) =>
            stream.WriteChunkOnceAsync(chunk, withoutResponse, cancellationToken);
    }

    /// <summary>Gets the usable write payload size derived from the negotiated ATT MTU.</summary>
    internal int PayloadSize => Volatile.Read(ref _payloadSize);

    /// <summary>
    /// Applies the ATT MTU reported by the stack. Values too small to carry a
    /// payload fall back to the 20-byte default.
    /// </summary>
    internal void ApplyNegotiatedMtu(int mtu)
    {
        lock (_sync)
        {
            _mtuReported = true;
        }

        var payloadSize = mtu > AttHeaderSize
            ? Math.Min(mtu - AttHeaderSize, MaxPayloadSize)
            : DefaultPayloadSize;
        Volatile.Write(ref _payloadSize, payloadSize);
    }

    /// <summary>
    /// Applies the default MTU when no MTU report arrived before the negotiation
    /// deadline. Returns <c>false</c> when the MTU was already reported.
    /// </summary>
    internal bool TryApplyFallbackMtu()
    {
        lock (_sync)
        {
            if (_mtuReported)
            {
                return false;
            }

            _mtuReported = true;
        }

        Volatile.Write(ref _payloadSize, DefaultPayloadSize);
        return true;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    internal void CompleteWrite(GattStatus status)
    {
        TaskCompletionSource<GattStatus>? writeTcs;
        var releaseGate = false;
        lock (_sync)
        {
            writeTcs = _writeCompletion;
            _writeCompletion = null;
            if (writeTcs is not null && _writeAbandoned)
            {
                _writeAbandoned = false;
                releaseGate = true;
            }
        }

        if (writeTcs is null)
        {
            return;
        }

        writeTcs.TrySetResult(status);

        if (releaseGate)
        {
            _writeGate.Release();
        }
    }

    internal void OnRemoteClosed()
    {
        var exception = new IOException("BLE GATT connection closed by the remote device.");
        _rxChannel.Writer.TryComplete(exception);

        lock (_sync)
        {
            _writeCompletion?.TrySetException(exception);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (disposing)
        {
            _rxChannel.Writer.TryComplete();

            lock (_sync)
            {
                _writeCompletion?.TrySetException(new ObjectDisposedException(nameof(BluetoothLeGattDuplexStream)));
                _writeCompletion = null;
            }

            try
            {
                _gatt.Disconnect();
            }
            catch
            {
                // Ignore disconnect failures during dispose.
            }

            try
            {
                _gatt.Close();
            }
            catch
            {
                // Ignore close failures during dispose.
            }

            _gatt.Dispose();
            _writeGate.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class GattCallback : BluetoothGattCallback
    {
        private readonly Guid _serviceUuid;
        private readonly Guid _rxCharacteristicUuid;
        private readonly Guid _txCharacteristicUuid;
        private readonly Channel<byte> _rxChannel;
        private readonly TaskCompletionSource<BluetoothLeGattDuplexStream> _readyTcs;

        private BluetoothLeGattDuplexStream? _stream;
        private BluetoothGattCharacteristic? _txCharacteristic;
        private int _aborted;

        public GattCallback(
            Guid serviceUuid,
            Guid rxCharacteristicUuid,
            Guid txCharacteristicUuid,
            Channel<byte> rxChannel,
            TaskCompletionSource<BluetoothLeGattDuplexStream> readyTcs)
        {
            _serviceUuid = serviceUuid;
            _rxCharacteristicUuid = rxCharacteristicUuid;
            _txCharacteristicUuid = txCharacteristicUuid;
            _rxChannel = rxChannel;
            _readyTcs = readyTcs;
        }

        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
        {
            if (gatt is null)
            {
                return;
            }

            var action = GattConnectionPolicy.Classify(
                (int)status,
                newState == ProfileState.Connected,
                newState == ProfileState.Disconnected);

            switch (action)
            {
                case GattConnectionAction.DiscoverServices:
                    if (!gatt.DiscoverServices())
                    {
                        FailReady(new IOException("BLE service discovery failed to start."));
                    }

                    return;

                case GattConnectionAction.Fail:
                    // Android can report an error status together with Connected
                    // (for example GATT_ERROR 133). Any non-success status must
                    // fail the pending connect, otherwise it waits forever
                    // (audit P-18).
                    FailConnection(
                        gatt,
                        new IOException(
                            $"BLE GATT connection failed with status {status} while {newState}."));
                    return;

                default:
                    return;
            }
        }

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
        {
            if (gatt is null || IsAborted)
            {
                return;
            }

            if (status != (int)GattStatus.Success)
            {
                FailReady(new IOException($"BLE service discovery failed with status {status}."));
                return;
            }

            try
            {
                var service = gatt.GetService(ToJavaUuid(_serviceUuid))
                    ?? throw new InvalidOperationException($"BLE service {_serviceUuid} was not found.");

                var rx = service.GetCharacteristic(ToJavaUuid(_rxCharacteristicUuid))
                    ?? throw new InvalidOperationException($"BLE RX characteristic {_rxCharacteristicUuid} was not found.");

                var tx = service.GetCharacteristic(ToJavaUuid(_txCharacteristicUuid))
                    ?? throw new InvalidOperationException($"BLE TX characteristic {_txCharacteristicUuid} was not found.");

                _txCharacteristic = tx;
                var stream = new BluetoothLeGattDuplexStream(gatt, rx, _rxChannel);
                _stream = stream;

                if (!gatt.SetCharacteristicNotification(tx, enable: true))
                {
                    throw new IOException("Failed to enable BLE TX characteristic notifications.");
                }

                var cccd = tx.GetDescriptor(CccdUuid)
                    ?? throw new InvalidOperationException("BLE TX CCCD descriptor was not found.");

                byte[] enableValue = [0x01, 0x00];
                var enableNotificationValue = BluetoothGattDescriptor.EnableNotificationValue;
                if (enableNotificationValue is not null && enableNotificationValue.Count >= 2)
                {
                    enableValue = [enableNotificationValue[0], enableNotificationValue[1]];
                }

                var descriptorStatus = gatt.WriteDescriptor(cccd, enableValue);
                if (descriptorStatus != (int)GattStatus.Success)
                {
                    throw new IOException($"Failed to write BLE CCCD to enable notifications (status {descriptorStatus}).");
                }
            }
            catch (Exception ex)
            {
                FailReady(ex);
            }
        }

        public override void OnDescriptorWrite(
            BluetoothGatt? gatt,
            BluetoothGattDescriptor? descriptor,
            GattStatus status)
        {
            if (_stream is null || IsAborted)
            {
                return;
            }

            if (status != (int)GattStatus.Success)
            {
                FailReady(new IOException($"BLE CCCD write failed with status {status}."));
                return;
            }

            // Negotiate the largest ATT MTU so whole KISS frames are not truncated.
            // The exchange is best-effort: when the stack cannot start it, or the
            // peer never answers, the stream falls back to the 20-byte default payload.
            if (gatt is null || !gatt.RequestMtu(RequestedAttMtu))
            {
                _stream.ApplyNegotiatedMtu(0);
                _readyTcs.TrySetResult(_stream);
                return;
            }

            _ = CompleteReadyAfterMtuTimeoutAsync(_stream, _readyTcs);
        }

        public override void OnMtuChanged(BluetoothGatt? gatt, int mtu, GattStatus status)
        {
            if (_stream is null || IsAborted)
            {
                return;
            }

            _stream.ApplyNegotiatedMtu(status == GattStatus.Success ? mtu : 0);
            _readyTcs.TrySetResult(_stream);
        }

        /// <summary>
        /// Completes the connection with the default payload size when no
        /// <see cref="OnMtuChanged"/> callback arrives within the negotiation window.
        /// </summary>
        private static async Task CompleteReadyAfterMtuTimeoutAsync(
            BluetoothLeGattDuplexStream stream,
            TaskCompletionSource<BluetoothLeGattDuplexStream> readyTcs)
        {
            await Task.Delay(MtuNegotiationTimeout).ConfigureAwait(false);

            if (stream.TryApplyFallbackMtu())
            {
                readyTcs.TrySetResult(stream);
            }
        }

        public override void OnCharacteristicChanged(
            BluetoothGatt? gatt,
            BluetoothGattCharacteristic? characteristic)
        {
            if (IsAborted || characteristic is null || _txCharacteristic is null)
            {
                return;
            }

            if (!UuidEquals(characteristic.Uuid, _txCharacteristic.Uuid))
            {
                return;
            }

#pragma warning disable CA1416, CA1422
            PublishValue(characteristic.GetValue());
#pragma warning restore CA1416, CA1422
        }

        public override void OnCharacteristicChanged(
            BluetoothGatt? gatt,
            BluetoothGattCharacteristic? characteristic,
            byte[]? value)
        {
            if (IsAborted || characteristic is null || _txCharacteristic is null)
            {
                return;
            }

            if (!UuidEquals(characteristic.Uuid, _txCharacteristic.Uuid))
            {
                return;
            }

            if (value is { Length: > 0 })
            {
                PublishValue(value);
            }
            else
            {
                OnCharacteristicChanged(gatt, characteristic);
            }
        }

        public override void OnCharacteristicWrite(
            BluetoothGatt? gatt,
            BluetoothGattCharacteristic? characteristic,
            GattStatus status)
        {
            _stream?.CompleteWrite(status);
        }

        private void PublishValue(byte[]? value)
        {
            if (value is null || value.Length == 0)
            {
                return;
            }

            foreach (var b in value)
            {
                if (!_rxChannel.Writer.TryWrite(b))
                {
                    break;
                }
            }
        }

        private bool IsAborted => Volatile.Read(ref _aborted) != 0;

        private void FailConnection(BluetoothGatt gatt, Exception exception)
        {
            _stream?.OnRemoteClosed();
            FailReady(exception);
            CloseGatt(gatt);
        }

        private void FailReady(Exception exception)
        {
            Interlocked.Exchange(ref _aborted, 1);
            _rxChannel.Writer.TryComplete(exception);
            _readyTcs.TrySetException(exception);
        }

        /// <summary>
        /// Releases the GATT client after a connection failure. Closing is
        /// idempotent because <see cref="BluetoothLeGattDuplexStream.ConnectAsync"/>
        /// repeats the cleanup when the ready task faults.
        /// </summary>
        private static void CloseGatt(BluetoothGatt gatt)
        {
            try
            {
                gatt.Disconnect();
            }
            catch
            {
                // Best-effort cleanup after a failed connection.
            }

            try
            {
                gatt.Close();
            }
            catch
            {
                // Best-effort cleanup after a failed connection.
            }
        }

        private static UUID ToJavaUuid(Guid guid) => UUID.FromString(guid.ToString())!;

        private static bool UuidEquals(UUID? left, UUID? right)
        {
            if (left is null || right is null)
            {
                return false;
            }

            return string.Equals(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using AetherAprs.Services.Bluetooth;
using AetherAprs.Transports.Kiss;

using Android.Bluetooth;
using Android.Content;

using Java.Util;
using Java.Util.Concurrent;

namespace AetherAprs.Android.Services.Transports;

/// <summary>
/// Duplex stream backed by a connected BLE GATT Nordic-UART-style pair of characteristics.
/// Read pulls from TX notifications; write pushes to the RX characteristic.
/// </summary>
internal sealed class BluetoothLeGattDuplexStream : Stream
{
    private static readonly UUID CccdUuid = UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;

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
    private readonly Lock _sync = new();
    private readonly BluetoothLeFrameWriter _frameWriter;
    private readonly GattWriteCallbackTracker _writeTracker = new();

    private TaskCompletionSource<GattStatus>? _writeCompletion;
    private bool _disposed;

    /// <summary>Usable bytes per WriteCharacteristic call, derived from the negotiated MTU.</summary>
    private int _payloadSize = GattPayloadSize.DefaultPayloadSize;

    /// <summary>Whether the MTU was reported (or assumed) for this connection.</summary>
    private bool _mtuReported;

    private BluetoothLeGattDuplexStream(
        BluetoothGatt gatt,
        BluetoothGattCharacteristic rxCharacteristic,
        Channel<byte> rxChannel,
        int payloadSize)
    {
        _gatt = gatt;
        _rxCharacteristic = rxCharacteristic;
        _rxChannel = rxChannel;
        _frameWriter = new BluetoothLeFrameWriter(new GattChunkWriter(this));
        _payloadSize = payloadSize;
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
            BluetoothGattConnectionSettings settings = new BluetoothGattConnectionSettings.Builder()
                .SetAutoConnectEnabled(false)
                .SetTransport((int)BluetoothTransports.Le)
                .Build() ?? throw new InvalidOperationException("The BLE connection settings could not be built.");
            IExecutor executor = context.MainExecutor
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
        KissConnectionContext kissContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        Context context = global::Android.App.Application.Context;
        Channel<byte> rxChannel = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        GattNegotiationProgress progress = new(
            kissContext,
            serviceUuid,
            rxCharacteristicUuid,
            txCharacteristicUuid);

        TaskCompletionSource<BluetoothLeGattDuplexStream> readyTcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        GattCallback callback = new(
            serviceUuid,
            rxCharacteristicUuid,
            txCharacteristicUuid,
            rxChannel,
            readyTcs,
            progress);

        progress.Connecting();

        BluetoothGatt gatt = StartGattConnection(device, context, callback)
            ?? throw new InvalidOperationException("Failed to start BLE GATT connection.");
        await using (cancellationToken.Register(() =>
        {
            readyTcs.TrySetCanceled(cancellationToken);
            progress.Cancelled(KissConnectionStage.ConnectingTransport);
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
                BluetoothLeGattDuplexStream stream = await readyTcs.Task.ConfigureAwait(false);
                progress.Ready();
                return stream;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    gatt.Disconnect();
                    gatt.Close();
                }
                catch
                {
                    // Ignore cleanup failures while propagating cancellation.
                }

                gatt.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                if (!progress.HasReportedFailure)
                {
                    progress.Failed(KissConnectionStage.ConnectingTransport, ex);
                }

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

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.Length == 0)
        {
            return 0;
        }

        int written = 0;

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

    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.IsEmpty)
        {
            return;
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _frameWriter.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // A write-with-response whose callback has not arrived yet keeps the gate
            // held; the tracker tells the late callback to release it. Any other exit
            // (success, a rejection that never awaited a callback, or a write without
            // response) releases the gate here.
            if (!_writeTracker.HasPendingWrite)
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
        GattWriteType writeType = withoutResponse ? GattWriteType.NoResponse : GattWriteType.Default;

        if (writeType == GattWriteType.Default)
        {
            TaskCompletionSource<GattStatus> writeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            // Registering here (rather than in the callback) is what lets a late
            // acknowledgement be recognized as stale instead of being applied to the
            // next write.
            object token = _writeTracker.Register();
            lock (_sync)
            {
                _writeCompletion = writeTcs;
            }

            int startStatus = _gatt.WriteCharacteristic(_rxCharacteristic, chunk.ToArray(), (int)writeType);
            if (startStatus != (int)GattStatus.Success)
            {
                _writeTracker.TryCancel(token);
                lock (_sync)
                {
                    if (ReferenceEquals(_writeCompletion, writeTcs))
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
                // Keep the gate held until the acknowledgement for this write arrives:
                // the stack does not identify the callback, so a second write started
                // now would consume the wrong acknowledgement.
                _writeTracker.Abandon(token);

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
    /// <returns>The payload size that was applied.</returns>
    internal int ApplyNegotiatedMtu(int mtu)
    {
        int reported = mtu > GattPayloadSize.AttHeaderSize ? mtu : 0;
        int payloadSize = GattPayloadSize.FromMtu(mtu);

        lock (_sync)
        {
            _mtuReported = true;
        }

        // Only a value the stack actually reported is passed on, so the negotiated
        // detail distinguishes "peer said 23" from "nobody answered".
        NegotiatedMtu = reported;
        Volatile.Write(ref _payloadSize, payloadSize);
        return payloadSize;
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

        NegotiatedMtu = null;
        Volatile.Write(ref _payloadSize, GattPayloadSize.DefaultPayloadSize);
        return true;
    }

    /// <summary>ATT MTU the stack reported, or null when none arrived.</summary>
    internal int? NegotiatedMtu { get; private set; }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    internal void CompleteWrite(GattStatus status)
    {
        // The callback carries no identifier, so it can only be matched against the
        // single pending write. A callback that arrives with nothing pending is stale
        // and must not release the gate or complete a later write.
        GattWriteCallback callback = _writeTracker.Complete();
        TaskCompletionSource<GattStatus>? writeTcs;
        lock (_sync)
        {
            writeTcs = _writeCompletion;
            _writeCompletion = null;
        }

        if (writeTcs is null)
        {
            if (callback.ReleasesGate)
            {
                _writeGate.Release();
            }

            return;
        }

        writeTcs.TrySetResult(status);

        if (callback.ReleasesGate)
        {
            _writeGate.Release();
        }
    }

    internal void OnRemoteClosed()
    {
        IOException exception = new("BLE GATT connection closed by the remote device.");
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

    private sealed class GattCallback(
        Guid serviceUuid,
        Guid rxCharacteristicUuid,
        Guid txCharacteristicUuid,
        Channel<byte> rxChannel,
        TaskCompletionSource<BluetoothLeGattDuplexStream> readyTcs,
        GattNegotiationProgress progress) : BluetoothGattCallback
    {
        private readonly Guid _serviceUuid = serviceUuid;
        private readonly Guid _rxCharacteristicUuid = rxCharacteristicUuid;
        private readonly Guid _txCharacteristicUuid = txCharacteristicUuid;
        private readonly Channel<byte> _rxChannel = rxChannel;
        private readonly TaskCompletionSource<BluetoothLeGattDuplexStream> _readyTcs = readyTcs;
        private readonly GattNegotiationProgress _progress = progress;

        private BluetoothLeGattDuplexStream? _stream;
        private BluetoothGattCharacteristic? _txCharacteristic;
        private int _aborted;

        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
        {
            if (gatt is null)
            {
                return;
            }

            GattConnectionAction action = GattConnectionPolicy.Classify(
                (int)status,
                newState == ProfileState.Connected,
                newState == ProfileState.Disconnected);

            switch (action)
            {
                case GattConnectionAction.DiscoverServices:
                    _progress.Discovering();

                    if (!gatt.DiscoverServices())
                    {
                        FailReady(
                            KissConnectionStage.DiscoveringServices,
                            new IOException("BLE service discovery failed to start."));
                    }

                    return;

                case GattConnectionAction.Fail:
                    // Android can report an error status together with Connected
                    // (for example GATT_ERROR 133). Any non-success status must
                    // fail the pending connect, otherwise it waits forever
                    //.
                    FailConnection(
                        gatt,
                        KissConnectionStage.ConnectingTransport,
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
                FailReady(
                    KissConnectionStage.DiscoveringServices,
                    new IOException($"BLE service discovery failed with status {status}."));
                return;
            }

            try
            {
                BluetoothGattService service = gatt.GetService(ToJavaUuid(_serviceUuid))
                    ?? throw new InvalidOperationException($"BLE service {_serviceUuid} was not found.");

                BluetoothGattCharacteristic rx = service.GetCharacteristic(ToJavaUuid(_rxCharacteristicUuid))
                    ?? throw new InvalidOperationException($"BLE RX characteristic {_rxCharacteristicUuid} was not found.");

                BluetoothGattCharacteristic tx = service.GetCharacteristic(ToJavaUuid(_txCharacteristicUuid))
                    ?? throw new InvalidOperationException($"BLE TX characteristic {_txCharacteristicUuid} was not found.");

                // The write mode decides how the frame writer paces chunks, so it is
                // read from the RX properties before the first frame can be written.
                _progress.DiscoverCharacteristics(
                    (rx.Properties & GattProperty.WriteNoResponse) != 0);

                _txCharacteristic = tx;
                BluetoothLeGattDuplexStream stream = new(
                    gatt,
                    rx,
                    _rxChannel,
                    GattPayloadSize.DefaultPayloadSize);
                _stream = stream;

                if (!gatt.SetCharacteristicNotification(tx, enable: true))
                {
                    throw new IOException("Failed to enable BLE TX characteristic notifications.");
                }

                BluetoothGattDescriptor cccd = tx.GetDescriptor(CccdUuid)
                    ?? throw new InvalidOperationException("BLE TX CCCD descriptor was not found.");

                byte[] enableValue = [0x01, 0x00];
                IList<byte>? enableNotificationValue = BluetoothGattDescriptor.EnableNotificationValue;
                if (enableNotificationValue is not null && enableNotificationValue.Count >= 2)
                {
                    enableValue = [enableNotificationValue[0], enableNotificationValue[1]];
                }

                int descriptorStatus = gatt.WriteDescriptor(cccd, enableValue);
                if (descriptorStatus != (int)GattStatus.Success)
                {
                    throw new IOException($"Failed to write BLE CCCD to enable notifications (status {descriptorStatus}).");
                }
            }
            catch (Exception ex)
            {
                FailReady(KissConnectionStage.DiscoveringServices, ex);
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
                FailReady(
                    KissConnectionStage.WritingConfiguration,
                    new IOException($"BLE CCCD write failed with status {status}."));
                return;
            }

            _progress.ConfigurationWritten();

            // Negotiate the largest ATT MTU so whole KISS frames are not truncated.
            // The exchange is best-effort: when the stack cannot start it, or the
            // peer never answers, the stream falls back to the 20-byte default payload.
            if (gatt is null || !gatt.RequestMtu(GattPayloadSize.RequestedAttMtu))
            {
                int payloadSize = _stream.ApplyNegotiatedMtu(0);
                _progress.PayloadNegotiated(null, payloadSize);
                _readyTcs.TrySetResult(_stream);
                return;
            }

            _ = CompleteReadyAfterMtuTimeoutAsync(_stream, _readyTcs, _progress);
        }

        public override void OnMtuChanged(BluetoothGatt? gatt, int mtu, GattStatus status)
        {
            if (_stream is null || IsAborted)
            {
                return;
            }

            bool succeeded = status == GattStatus.Success;
            int payloadSize = _stream.ApplyNegotiatedMtu(succeeded ? mtu : 0);
            _progress.PayloadNegotiated(succeeded ? mtu : null, payloadSize);
            _readyTcs.TrySetResult(_stream);
        }

        /// <summary>
        /// Completes the connection with the default payload size when no
        /// <see cref="OnMtuChanged"/> callback arrives within the negotiation window.
        /// </summary>
        private static async Task CompleteReadyAfterMtuTimeoutAsync(
            BluetoothLeGattDuplexStream stream,
            TaskCompletionSource<BluetoothLeGattDuplexStream> readyTcs,
            GattNegotiationProgress progress)
        {
            await Task.Delay(MtuNegotiationTimeout).ConfigureAwait(false);

            if (stream.TryApplyFallbackMtu())
            {
                progress.PayloadNegotiated(null, stream.PayloadSize);
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
            GattStatus status) => _stream?.CompleteWrite(status);

        private void PublishValue(byte[]? value)
        {
            if (value is null || value.Length == 0)
            {
                return;
            }

            foreach (byte b in value)
            {
                if (!_rxChannel.Writer.TryWrite(b))
                {
                    break;
                }
            }
        }

        private bool IsAborted => Volatile.Read(ref _aborted) != 0;

        private void FailConnection(
            BluetoothGatt gatt,
            KissConnectionStage stage,
            Exception exception)
        {
            _stream?.OnRemoteClosed();
            FailReady(stage, exception);
            CloseGatt(gatt);
        }

        private void FailReady(KissConnectionStage stage, Exception exception)
        {
            // Report before completing the ready task, so a caller that awaits the
            // connect and then reads the recorder already sees the failing stage.
            _progress.Failed(stage, exception);
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

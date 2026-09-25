// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Android.Bluetooth;
using Java.Util;
using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services;

/// <summary>
/// Duplex stream backed by a connected BLE GATT Nordic-UART-style pair of characteristics.
/// Read pulls from TX notifications; write pushes to the RX characteristic.
/// </summary>
internal sealed class BluetoothLeGattDuplexStream : Stream
{
    private static readonly UUID CccdUuid = UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;

    private readonly BluetoothGatt _gatt;
    private readonly BluetoothGattCharacteristic _rxCharacteristic;
    private readonly Channel<byte> _rxChannel;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _sync = new();

    private TaskCompletionSource<bool>? _writeCompletion;
    private bool _disposed;

    private BluetoothLeGattDuplexStream(
        BluetoothGatt gatt,
        BluetoothGattCharacteristic rxCharacteristic,
        Channel<byte> rxChannel)
    {
        _gatt = gatt;
        _rxCharacteristic = rxCharacteristic;
        _rxChannel = rxChannel;
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
        var gatt = device.ConnectGatt(context, autoConnect: false, callback)
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
        try
        {
            var payload = buffer.ToArray();
            var writeType = (_rxCharacteristic.Properties & GattProperty.WriteNoResponse) != 0
                ? GattWriteType.NoResponse
                : GattWriteType.Default;

            if (writeType == GattWriteType.Default)
            {
                var writeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_sync)
                {
                    _writeCompletion = writeTcs;
                }

                var status = _gatt.WriteCharacteristic(_rxCharacteristic, payload, (int)writeType);
                if (status != (int)GattStatus.Success)
                {
                    lock (_sync)
                    {
                        _writeCompletion = null;
                    }

                    throw new IOException($"BLE WriteCharacteristic failed to start with status {status}.");
                }

                await using (cancellationToken.Register(() => writeTcs.TrySetCanceled(cancellationToken)))
                {
                    await writeTcs.Task.ConfigureAwait(false);
                }
            }
            else
            {
                var status = _gatt.WriteCharacteristic(_rxCharacteristic, payload, (int)writeType);
                if (status != (int)GattStatus.Success)
                {
                    throw new IOException($"BLE WriteCharacteristic (no response) failed to start with status {status}.");
                }
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    internal void CompleteWrite(bool success, GattStatus status)
    {
        TaskCompletionSource<bool>? writeTcs;
        lock (_sync)
        {
            writeTcs = _writeCompletion;
            _writeCompletion = null;
        }

        if (writeTcs is null)
        {
            return;
        }

        if (success && status == GattStatus.Success)
        {
            writeTcs.TrySetResult(true);
        }
        else
        {
            writeTcs.TrySetException(new IOException($"BLE characteristic write failed with status {status}."));
        }
    }

    internal void OnRemoteClosed()
    {
        _rxChannel.Writer.TryComplete();
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

            if (newState == ProfileState.Connected && status == GattStatus.Success)
            {
                if (!gatt.DiscoverServices())
                {
                    FailReady(new IOException("BLE service discovery failed to start."));
                }

                return;
            }

            if (newState == ProfileState.Disconnected)
            {
                _stream?.OnRemoteClosed();
                FailReady(new IOException($"BLE GATT disconnected with status {status}."));
            }
        }

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
        {
            if (gatt is null)
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
            if (_stream is null)
            {
                return;
            }

            if (status != (int)GattStatus.Success)
            {
                FailReady(new IOException($"BLE CCCD write failed with status {status}."));
                return;
            }

            _readyTcs.TrySetResult(_stream);
        }

        public override void OnCharacteristicChanged(
            BluetoothGatt? gatt,
            BluetoothGattCharacteristic? characteristic)
        {
            if (characteristic is null || _txCharacteristic is null)
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
            if (characteristic is null || _txCharacteristic is null)
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
            _stream?.CompleteWrite(status == GattStatus.Success, status);
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

        private void FailReady(Exception exception)
        {
            _rxChannel.Writer.TryComplete(exception);
            _readyTcs.TrySetException(exception);
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
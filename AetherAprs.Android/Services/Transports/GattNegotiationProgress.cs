// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Transports.Kiss;
using System;
using System.Threading;

namespace AetherAprs.Android.Services.Transports;

/// <summary>
/// Reports a Bluetooth LE GATT negotiation stage by stage and accumulates what was
/// negotiated, so a failed connect names the step that stopped rather than a bare
/// stack trace.
/// </summary>
/// <remarks>
/// The GATT callbacks arrive on the Android stack's thread in an order the caller
/// does not control: connection, service discovery, characteristic lookup, CCCD
/// write, MTU request and MTU report. Keeping the accumulation here means the stream
/// and its callback only forward events, and every stage can be unit-tested without
/// a device.
/// </remarks>
internal sealed class GattNegotiationProgress
{
    private readonly KissConnectionContext _context;
    private readonly Guid _serviceUuid;
    private readonly Guid _rxCharacteristicUuid;
    private readonly Guid _txCharacteristicUuid;

    private bool? _notificationsEnabled;
    private KissGattWriteMode _writeMode = KissGattWriteMode.Unknown;
    private int? _attMtu;
    private int? _payloadSize;
    private int _failureReported;
    private KissConnectionProgress? _latest;

    /// <summary>
    /// Creates a reporter for one attempt over the configured GATT attributes.
    /// </summary>
    /// <param name="context">Per-attempt progress context from the session.</param>
    /// <param name="serviceUuid">GATT service the characteristics belong to.</param>
    /// <param name="rxCharacteristicUuid">Characteristic the host writes.</param>
    /// <param name="txCharacteristicUuid">Characteristic the peripheral notifies.</param>
    public GattNegotiationProgress(
        KissConnectionContext context,
        Guid serviceUuid,
        Guid rxCharacteristicUuid,
        Guid txCharacteristicUuid)
    {
        _context = context;
        _serviceUuid = serviceUuid;
        _rxCharacteristicUuid = rxCharacteristicUuid;
        _txCharacteristicUuid = txCharacteristicUuid;
    }

    /// <summary>
    /// Whether a failure was already reported, so the enclosing connect does not
    /// replace the precise stage with a generic one.
    /// </summary>
    public bool HasReportedFailure => Volatile.Read(ref _failureReported) != 0;

    /// <summary>The last report sent, so the connect can rethrow it with its stage.</summary>
    public KissConnectionProgress? Latest => Volatile.Read(ref _latest);

    /// <summary>What has been negotiated so far, for the ready report.</summary>
    public KissBluetoothLeDetail Detail => new(
        _serviceUuid,
        _rxCharacteristicUuid,
        _txCharacteristicUuid,
        _notificationsEnabled,
        _writeMode,
        _attMtu,
        _payloadSize);

    /// <summary>The channel write mode, once the RX properties were read.</summary>
    public KissGattWriteMode WriteMode => _writeMode;

    /// <summary>Usable bytes per write, once the ATT MTU is known.</summary>
    public int? PayloadSize => _payloadSize;

    /// <summary>Reports that the GATT client is connecting.</summary>
    public void Connecting() =>
        Report(KissConnectionStage.ConnectingTransport);

    /// <summary>Reports that service discovery was started.</summary>
    public void Discovering() =>
        Report(KissConnectionStage.DiscoveringServices);

    /// <summary>
    /// Records the write mode read from the RX characteristic properties, before the
    /// CCCD is written. A characteristic that accepts write-without-response is
    /// paced by the frame writer, so the mode has to be known before the first frame.
    /// </summary>
    /// <param name="supportsWriteWithoutResponse">Whether the RX property was set.</param>
    public void DiscoverCharacteristics(bool supportsWriteWithoutResponse)
    {
        _writeMode = supportsWriteWithoutResponse
            ? KissGattWriteMode.WithoutResponse
            : KissGattWriteMode.WithResponse;

        Report(KissConnectionStage.WritingConfiguration);
    }

    /// <summary>
    /// Records that the CCCD write (notification enable) was accepted and reports
    /// the MTU negotiation stage.
    /// </summary>
    public void ConfigurationWritten()
    {
        _notificationsEnabled = true;
        Report(KissConnectionStage.NegotiatingPayload);
    }

    /// <summary>
    /// Records the negotiated ATT MTU and the payload size derived from it.
    /// </summary>
    /// <param name="attMtu">MTU reported by the stack, or null when none arrived.</param>
    /// <param name="payloadSize">Usable bytes per write.</param>
    public void PayloadNegotiated(int? attMtu, int payloadSize)
    {
        _attMtu = attMtu;
        _payloadSize = payloadSize;
    }

    /// <summary>Reports that the stream is open and usable.</summary>
    public void Ready() =>
        Report(
            KissConnectionStage.Ready,
            KissConnectionOutcome.Succeeded);

    /// <summary>Reports a failure at the stage that could not be completed.</summary>
    /// <param name="stage">Stage the attempt stopped at.</param>
    /// <param name="reason">Technical failure text, never message content.</param>
    public void Failed(KissConnectionStage stage, string reason)
    {
        Interlocked.Exchange(ref _failureReported, 1);

        Report(stage, KissConnectionOutcome.Failed, reason);
    }

    /// <summary>Reports a failure at the stage that could not be completed.</summary>
    /// <param name="stage">Stage the attempt stopped at.</param>
    /// <param name="exception">Failure that ended the attempt.</param>
    public void Failed(KissConnectionStage stage, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Failed(stage, exception.Message);
    }

    /// <summary>Reports a cancelled attempt at the stage it was waiting in.</summary>
    public void Cancelled(KissConnectionStage stage) =>
        Report(stage, KissConnectionOutcome.Cancelled);

    private void Report(
        KissConnectionStage stage,
        KissConnectionOutcome outcome = KissConnectionOutcome.InProgress,
        string? failure = null)
    {
        // The detail is stamped on every report so a consumer that only keeps the last
        // one still shows what had been negotiated when the attempt stopped.
        KissConnectionProgress progress = new()
        {
            Transport = KissTransportKind.BluetoothLe,
            Stage = stage,
            Outcome = outcome,
            Failure = failure,
            BluetoothLe = Detail
        };

        Volatile.Write(ref _latest, progress);
        _context.Report(progress);
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using AetherAprs.Configuration;

namespace AetherAprs.Transports.Kiss;

/// <summary>
/// Transport family that a KISS connection is being opened over.
/// </summary>
public enum KissTransportKind
{
    /// <summary>The family is not known yet.</summary>
    Unknown,

    /// <summary>A TCP socket to a KISS TNC server.</summary>
    Tcp,

    /// <summary>Bluetooth Classic SPP (RFCOMM).</summary>
    BluetoothClassicSpp,

    /// <summary>Bluetooth LE GATT, for example Nordic UART or the LoRa APRS tracker profile.</summary>
    BluetoothLe
}

/// <summary>
/// Stage a connection attempt has reached. The stages are ordered the way a
/// successful attempt walks through them; a failure reports the stage it did not
/// get past, which is what makes a bare exception actionable.
/// </summary>
public enum KissConnectionStage
{
    /// <summary>No attempt has been made.</summary>
    Idle,

    /// <summary>
    /// Settings and platform support are being checked, before any device or socket
    /// is touched. An unsupported platform stops here.
    /// </summary>
    PreparingTransport,

    /// <summary>The runtime permission the transport needs is being requested.</summary>
    RequestingPermission,

    /// <summary>The configured device address is being resolved against the adapter.</summary>
    ResolvingDevice,

    /// <summary>The socket or GATT client is being connected.</summary>
    ConnectingTransport,

    /// <summary>Services and characteristics are being discovered (Bluetooth LE).</summary>
    DiscoveringServices,

    /// <summary>Notifications are being enabled by writing the CCCD (Bluetooth LE).</summary>
    WritingConfiguration,

    /// <summary>The ATT MTU is being negotiated (Bluetooth LE).</summary>
    NegotiatingPayload,

    /// <summary>The stream is open and usable.</summary>
    Ready,

    /// <summary>The attempt failed; <see cref="KissConnectionProgress.Failure"/> says why.</summary>
    Failed,

    /// <summary>The attempt was cancelled before it finished.</summary>
    Cancelled
}

/// <summary>
/// How a chunk is written to the Bluetooth LE RX characteristic.
/// </summary>
public enum KissGattWriteMode
{
    /// <summary>The characteristic properties have not been read yet.</summary>
    Unknown,

    /// <summary>Write-with-response: the stack serializes on the ATT acknowledgement.</summary>
    WithResponse,

    /// <summary>
    /// Write-without-response: no ATT-level flow control, so chunks are paced by
    /// <see cref="AetherAprs.Services.Bluetooth.BluetoothLeFrameWriter"/>.
    /// </summary>
    WithoutResponse
}

/// <summary>
/// State of the Bluetooth Classic SPP link.
/// </summary>
public enum KissSppLinkState
{
    /// <summary>The link state is not known yet.</summary>
    Unknown,

    /// <summary>The RFCOMM socket is connecting.</summary>
    Connecting,

    /// <summary>The RFCOMM socket reported a connected link.</summary>
    Connected
}

/// <summary>
/// Outcome of one connection attempt.
/// </summary>
public enum KissConnectionOutcome
{
    /// <summary>The attempt is still running.</summary>
    InProgress,

    /// <summary>The stream is open.</summary>
    Succeeded,

    /// <summary>The attempt failed; the reported stage is where it stopped.</summary>
    Failed,

    /// <summary>The attempt was cancelled.</summary>
    Cancelled
}

/// <summary>
/// What was negotiated on a Bluetooth LE link, filled in stage by stage.
/// </summary>
/// <param name="ServiceUuid">GATT service the characteristics were taken from.</param>
/// <param name="RxCharacteristicUuid">Characteristic written by the host.</param>
/// <param name="TxCharacteristicUuid">Characteristic notified by the peripheral.</param>
/// <param name="NotificationsEnabled">Whether the CCCD write was accepted.</param>
/// <param name="WriteMode">Write mode chosen from the characteristic properties.</param>
/// <param name="AttMtu">ATT MTU reported by the stack, when it was reported.</param>
/// <param name="PayloadSize">Usable bytes per write, derived from the MTU.</param>
public readonly record struct KissBluetoothLeDetail(
    Guid ServiceUuid,
    Guid RxCharacteristicUuid,
    Guid TxCharacteristicUuid,
    bool? NotificationsEnabled,
    KissGattWriteMode WriteMode,
    int? AttMtu,
    int? PayloadSize);

/// <summary>
/// One report of what a connection attempt reached, including the negotiated
/// detail. It is a plain data type so a connector can build it on a platform
/// callback thread and a view model can render it without sharing state.
/// </summary>
public sealed record KissConnectionProgress
{
    /// <summary>The stage the attempt reached.</summary>
    public required KissConnectionStage Stage { get; init; }

    /// <summary>The transport family the attempt belongs to.</summary>
    public KissTransportKind Transport { get; init; } = KissTransportKind.Unknown;

    /// <summary>Whether this reports progress, success, failure or cancellation.</summary>
    public KissConnectionOutcome Outcome { get; init; } = KissConnectionOutcome.InProgress;

    /// <summary>
    /// One-based attempt number within the running session. A value above one means
    /// the session is reconnecting after an earlier attempt failed.
    /// </summary>
    public int Attempt { get; init; } = 1;

    /// <summary>What was negotiated, for a Bluetooth LE attempt.</summary>
    public KissBluetoothLeDetail? BluetoothLe { get; init; }

    /// <summary>The SPP link state, for a Bluetooth Classic attempt.</summary>
    public KissSppLinkState? SppLinkState { get; init; }

    /// <summary>
    /// Technical failure text for diagnostics. It never contains credentials or
    /// message content and is not shown to the operator verbatim.
    /// </summary>
    public string? Failure { get; init; }

    /// <summary>Whether the attempt reached a terminal outcome.</summary>
    public bool IsTerminal => Outcome is not KissConnectionOutcome.InProgress;

    /// <summary>Whether the reported stage is a failure.</summary>
    public bool IsFailure => Outcome == KissConnectionOutcome.Failed;

    /// <summary>Reports an intermediate stage of an attempt still in progress.</summary>
    public static KissConnectionProgress Progress(
        KissTransportKind transport,
        KissConnectionStage stage,
        int attempt = 1) =>
        new() { Transport = transport, Stage = stage, Attempt = attempt };

    /// <summary>Reports that the stream is open and usable.</summary>
    public static KissConnectionProgress Ready(
        KissTransportKind transport,
        KissBluetoothLeDetail? bluetoothLe = null,
        KissSppLinkState? sppLinkState = null,
        int attempt = 1) =>
        new()
        {
            Transport = transport,
            Stage = KissConnectionStage.Ready,
            Outcome = KissConnectionOutcome.Succeeded,
            Attempt = attempt,
            BluetoothLe = bluetoothLe,
            SppLinkState = sppLinkState
        };

    /// <summary>
    /// Reports a failed attempt at the stage that could not be completed.
    /// </summary>
    public static KissConnectionProgress Failed(
        KissTransportKind transport,
        KissConnectionStage stage,
        string? failure,
        int attempt = 1) =>
        new()
        {
            Transport = transport,
            Stage = stage,
            Outcome = KissConnectionOutcome.Failed,
            Attempt = attempt,
            Failure = failure
        };

    /// <summary>Reports a cancelled attempt.</summary>
    public static KissConnectionProgress Cancelled(
        KissTransportKind transport,
        KissConnectionStage stage,
        KissSppLinkState? sppLinkState = null,
        int attempt = 1) =>
        new()
        {
            Transport = transport,
            Stage = stage,
            Outcome = KissConnectionOutcome.Cancelled,
            SppLinkState = sppLinkState,
            Attempt = attempt
        };
}

/// <summary>
/// Receives <see cref="KissConnectionProgress"/> reports from a connector.
/// </summary>
/// <remarks>
/// Implementations are called from whichever thread the transport uses (for
/// Bluetooth LE that is an Android GATT callback thread), so an implementation
/// must be thread-safe and must not block.
/// </remarks>
public interface IKissConnectionObserver
{
    /// <summary>Records one report.</summary>
    void Report(KissConnectionProgress progress);
}

/// <summary>
/// Per-attempt context handed to a connector: where to report progress and which
/// attempt number the session is on.
/// </summary>
/// <remarks>
/// A connector only ever reads this value; the session owns the observer and the
/// attempt counter. Passing the context (rather than a bare observer) means a
/// reconnecting session's reports carry the attempt number without the connector
/// keeping state of its own.
/// </remarks>
public readonly record struct KissConnectionContext(IKissConnectionObserver? Observer, int Attempt)
{
    /// <summary>A context that reports nowhere, for a one-off connection.</summary>
    public static KissConnectionContext None => new(null, 1);

    /// <summary>
    /// The attempt number reported to the observer. A default-constructed context
    /// carries zero, which is not a meaningful attempt, so it is reported as the
    /// first one.
    /// </summary>
    public int EffectiveAttempt => Attempt > 0 ? Attempt : 1;

    /// <summary>Reports an intermediate stage of this attempt.</summary>
    public void Report(KissTransportKind transport, KissConnectionStage stage) =>
        Observer?.Report(KissConnectionProgress.Progress(transport, stage, EffectiveAttempt));

    /// <summary>Reports a fully built progress record for this attempt.</summary>
    public void Report(KissConnectionProgress progress) =>
        Observer?.Report(
            progress.Attempt == EffectiveAttempt ? progress : progress with { Attempt = EffectiveAttempt });
}

/// <summary>
/// Maps a settings type onto the transport family it belongs to.
/// </summary>
public static class KissTransportKindResolver
{
    /// <summary>Returns the transport family of <paramref name="settings"/>.</summary>
    public static KissTransportKind FromSettings(IKissTransportSettings? settings) => settings switch
    {
        TcpKissTransportSettings => KissTransportKind.Tcp,
        BluetoothClassicKissTransportSettings => KissTransportKind.BluetoothClassicSpp,
        BluetoothLeKissTransportSettings => KissTransportKind.BluetoothLe,
        _ => KissTransportKind.Unknown
    };
}

/// <summary>
/// Bounded, thread-safe collector of connection reports. The session keeps one
/// per attempt so a failure can name its stage and the port editor can show the
/// negotiated detail.
/// </summary>
public sealed class KissConnectionProgressRecorder : IKissConnectionObserver
{
    /// <summary>Number of reports kept; older reports are dropped.</summary>
    public const int HistoryLimit = 32;

    private readonly Lock _sync = new();
    private readonly List<KissConnectionProgress> _history = [];

    /// <summary>The most recent report, or <see langword="null"/> when none arrived.</summary>
    public KissConnectionProgress? Latest
    {
        get
        {
            lock (_sync)
            {
                return _history.Count > 0 ? _history[^1] : null;
            }
        }
    }

    /// <summary>Reports in arrival order, oldest first.</summary>
    public IReadOnlyList<KissConnectionProgress> History
    {
        get
        {
            lock (_sync)
            {
                return [.. _history];
            }
        }
    }

    /// <inheritdoc />
    public void Report(KissConnectionProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        lock (_sync)
        {
            _history.Add(progress);
            if (_history.Count > HistoryLimit)
            {
                _history.RemoveRange(0, _history.Count - HistoryLimit);
            }
        }
    }
}

/// <summary>
/// A connection failure that keeps the stage the attempt reached, so the caller
/// can report "Bluetooth LE stopped while discovering services" instead of a bare
/// stack trace.
/// </summary>
public sealed class KissConnectionException : IOException
{
    public KissConnectionException(KissConnectionProgress progress, Exception innerException)
        : base(BuildMessage(progress, innerException), innerException)
    {
        Progress = progress;
    }

    /// <summary>The last report of the failed attempt.</summary>
    public KissConnectionProgress Progress { get; }

    /// <summary>The stage that could not be completed.</summary>
    public KissConnectionStage Stage => Progress.Stage;

    private static string BuildMessage(KissConnectionProgress progress, Exception innerException)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return $"{progress.Transport} connection stopped at {progress.Stage} on attempt {progress.Attempt}: {innerException.Message}";
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Localization;
using System;
using System.Collections.Generic;

namespace AetherAprs.Transports.Kiss;

/// <summary>
/// Renders a <see cref="KissConnectionProgress"/> as the two lines an operator sees:
/// what the connection reached and what was negotiated.
/// </summary>
/// <remarks>
/// The renderer is kept separate from the view model so the mapping from stage to
/// localized text can be asserted without constructing a page and its services.
/// Every string comes from the resource files, so a new stage cannot ship in English
/// only.
/// </remarks>
public static class KissConnectionStatusText
{
    /// <summary>
    /// Names the stage, and the failure reason when the attempt stopped.
    /// </summary>
    /// <param name="progress">The report to describe.</param>
    /// <returns>A localized one-line summary.</returns>
    public static string DescribeStage(KissConnectionProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return progress.Outcome switch
        {
            KissConnectionOutcome.Failed => Strings.Format(
                "ConnectionFailedAtStage",
                Strings.Get(StageKey(progress.Stage))),
            KissConnectionOutcome.Cancelled => Strings.Format(
                "ConnectionCancelledAtStage",
                Strings.Get(StageKey(progress.Stage))),
            _ => Strings.Get(StageKey(progress.Stage))
        };
    }

    /// <summary>
    /// Describes what the attempt negotiated: the GATT service and characteristics,
    /// the ATT payload, the write mode, the notification state, the SPP link and the
    /// attempt number.
    /// </summary>
    /// <param name="progress">The report to describe.</param>
    /// <returns>
    /// A localized line, or an empty string when the report carries no detail.
    /// </returns>
    public static string DescribeDetail(KissConnectionProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        List<string> parts = [];

        if (progress.BluetoothLe is { } detail)
        {
            if (detail.ServiceUuid != Guid.Empty)
            {
                parts.Add(Strings.Format(
                    "ConnectionDetailGattService",
                    detail.ServiceUuid,
                    detail.RxCharacteristicUuid,
                    detail.TxCharacteristicUuid));
            }

            if (detail.PayloadSize is { } payloadSize && detail.AttMtu is { } attMtu)
            {
                parts.Add(Strings.Format("ConnectionDetailPayload", payloadSize, attMtu));
            }
            else if (detail.PayloadSize is { } fallbackPayload)
            {
                parts.Add(Strings.Format("ConnectionDetailPayloadNoMtu", fallbackPayload));
            }

            parts.Add(detail.WriteMode switch
            {
                KissGattWriteMode.WithoutResponse => Strings.Get("ConnectionDetailWriteWithoutResponse"),
                KissGattWriteMode.WithResponse => Strings.Get("ConnectionDetailWriteWithResponse"),
                _ => string.Empty
            });

            if (detail.NotificationsEnabled == true)
            {
                parts.Add(Strings.Get("ConnectionDetailNotifications"));
            }
        }

        if (progress.SppLinkState == KissSppLinkState.Connected)
        {
            parts.Add(Strings.Get("ConnectionDetailSppConnected"));
        }

        if (progress.Attempt > 1)
        {
            parts.Add(Strings.Format("ConnectionDetailAttempt", progress.Attempt));
        }

        parts.RemoveAll(part => string.IsNullOrEmpty(part));

        return string.Join(" · ", parts);
    }

    /// <summary>The resource key for a stage, so callers can reuse the name.</summary>
    /// <param name="stage">Stage to name.</param>
    public static string StageKey(KissConnectionStage stage) => stage switch
    {
        KissConnectionStage.PreparingTransport => "ConnectionStagePreparingTransport",
        KissConnectionStage.RequestingPermission => "ConnectionStageRequestingPermission",
        KissConnectionStage.ResolvingDevice => "ConnectionStageResolvingDevice",
        KissConnectionStage.ConnectingTransport => "ConnectionStageConnectingTransport",
        KissConnectionStage.DiscoveringServices => "ConnectionStageDiscoveringServices",
        KissConnectionStage.WritingConfiguration => "ConnectionStageWritingConfiguration",
        KissConnectionStage.NegotiatingPayload => "ConnectionStageNegotiatingPayload",
        KissConnectionStage.Ready => "ConnectionStageReady",
        KissConnectionStage.Cancelled => "ConnectionStageCancelled",
        KissConnectionStage.Failed => "ConnectionStageFailed",
        _ => "ConnectionStageIdle"
    };

    /// <summary>
    /// Builds the warning shown when the configured transport has no connector on
    /// this platform, so an unavailable route is stated instead of silently replaced.
    /// </summary>
    /// <param name="settingsType">Configured transport settings type.</param>
    public static string DescribeUnavailableTransport(Type settingsType)
    {
        ArgumentNullException.ThrowIfNull(settingsType);

        return Strings.Format("KissTransportUnavailable", TransportDisplayName(settingsType));
    }

    /// <summary>
    /// Localized name of a KISS transport settings type.
    /// </summary>
    /// <param name="settingsType">Transport settings type.</param>
    public static string TransportDisplayName(Type settingsType)
    {
        ArgumentNullException.ThrowIfNull(settingsType);

        if (settingsType == typeof(TcpKissTransportSettings))
        {
            return Strings.Get("TransportTcp");
        }

        if (settingsType == typeof(BluetoothClassicKissTransportSettings))
        {
            return Strings.Get("TransportBluetoothClassicSpp");
        }

        if (settingsType == typeof(BluetoothLeKissTransportSettings))
        {
            return Strings.Get("TransportBluetoothLe");
        }

        return settingsType.Name;
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Android.Services.Transports;

/// <summary>Action the GATT connection-state callback must take.</summary>
internal enum GattConnectionAction
{
    /// <summary>The event carries no actionable transition.</summary>
    Ignore,

    /// <summary>The link is up with a success status; start service discovery.</summary>
    DiscoverServices,

    /// <summary>The connection failed or dropped; fail the pending connect.</summary>
    Fail
}

/// <summary>
/// Classifies a GATT connection-state callback. Kept free of Android types so
/// the failure paths can be unit-tested.
/// </summary>
/// <remarks>
/// Android may report an error status together with <c>Connected</c> (for
/// example <c>GATT_ERROR</c> 133). Classifying only <c>Disconnected</c> as a
/// failure left the ready task pending forever, so any non-success status must
/// fail the pending connect regardless of the reported state.
/// </remarks>
internal static class GattConnectionPolicy
{
    /// <summary>Status code Android uses for a successful operation.</summary>
    internal const int SuccessStatus = 0;

    /// <summary>
    /// Maps one <c>OnConnectionStateChange</c> callback to the required action.
    /// </summary>
    /// <param name="status">Raw GATT status code from the callback.</param>
    /// <param name="isConnected">Whether the callback reported <c>Connected</c>.</param>
    /// <param name="isDisconnected">Whether the callback reported <c>Disconnected</c>.</param>
    public static GattConnectionAction Classify(int status, bool isConnected, bool isDisconnected)
    {
        if (status != SuccessStatus)
        {
            return GattConnectionAction.Fail;
        }

        if (isConnected)
        {
            return GattConnectionAction.DiscoverServices;
        }

        return isDisconnected ? GattConnectionAction.Fail : GattConnectionAction.Ignore;
    }
}

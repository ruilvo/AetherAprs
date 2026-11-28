// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Transports.Kiss;
using Xunit;

namespace AetherAprs.Tests.Transports.Kiss;

/// <summary>
/// The rendered connection detail is the only place an operator sees what a KISS
/// transport negotiated, so the GATT identity and the attempt number must reach it
/// from the progress record.
/// </summary>
public sealed class KissConnectionStatusTextTests
{
    private static readonly Guid ServiceUuid = Guid.Parse("0001aabb-1234-5678-9abc-def012345678");
    private static readonly Guid RxUuid = Guid.Parse("0002aabb-1234-5678-9abc-def012345678");
    private static readonly Guid TxUuid = Guid.Parse("0003aabb-1234-5678-9abc-def012345678");

    [Fact]
    public void DescribeDetail_RendersNegotiatedGattServiceAndCharacteristics()
    {
        KissConnectionProgress progress = KissConnectionProgress.Ready(
            KissTransportKind.BluetoothLe,
            new KissBluetoothLeDetail(
                ServiceUuid,
                RxUuid,
                TxUuid,
                NotificationsEnabled: true,
                KissGattWriteMode.WithResponse,
                AttMtu: 247,
                PayloadSize: 244));

        string detail = KissConnectionStatusText.DescribeDetail(progress);

        // The LilyGO profile switches all three UUIDs, so the operator must be able
        // to see which service the link actually used.
        Assert.Contains(ServiceUuid.ToString(), detail, StringComparison.Ordinal);
        Assert.Contains(RxUuid.ToString(), detail, StringComparison.Ordinal);
        Assert.Contains(TxUuid.ToString(), detail, StringComparison.Ordinal);
        Assert.Contains("244", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeDetail_OmitsTheServiceWhenNoUuidWasReported()
    {
        KissConnectionProgress progress = KissConnectionProgress.Ready(
            KissTransportKind.BluetoothLe,
            new KissBluetoothLeDetail(
                Guid.Empty,
                Guid.Empty,
                Guid.Empty,
                NotificationsEnabled: true,
                KissGattWriteMode.WithResponse,
                AttMtu: null,
                PayloadSize: 20));

        string detail = KissConnectionStatusText.DescribeDetail(progress);

        Assert.DoesNotContain(Guid.Empty.ToString(), detail, StringComparison.Ordinal);
        Assert.Contains("20", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeDetail_RendersTheAttemptNumberOnlyAboveOne()
    {
        KissConnectionProgress first = KissConnectionProgress.Progress(
            KissTransportKind.BluetoothLe,
            KissConnectionStage.ConnectingTransport,
            attempt: 1);
        KissConnectionProgress retry = KissConnectionProgress.Progress(
            KissTransportKind.BluetoothLe,
            KissConnectionStage.ConnectingTransport,
            attempt: 3);

        Assert.DoesNotContain("3", KissConnectionStatusText.DescribeDetail(first), StringComparison.Ordinal);
        Assert.Contains("3", KissConnectionStatusText.DescribeDetail(retry), StringComparison.Ordinal);
    }
}

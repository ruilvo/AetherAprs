// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;

using AetherAprs.Android.Services.Transports;
using AetherAprs.Transports.Kiss;

using Xunit;

namespace AetherAprs.Tests.Android;

/// <summary>
/// Regression tests: a Bluetooth LE connect has to name the GATT step that stopped
/// (service discovery, CCCD write, characteristic lookup, MTU negotiation) and
/// report what was negotiated, instead of surfacing a bare failure.
/// </summary>
public sealed class GattNegotiationProgressTests
{
    private static readonly Guid ServiceUuid = Guid.Parse("6e400001-b5a3-f393-e0a9-e50e24dcca9e");
    private static readonly Guid RxUuid = Guid.Parse("6e400002-b5a3-f393-e0a9-e50e24dcca9e");
    private static readonly Guid TxUuid = Guid.Parse("6e400003-b5a3-f393-e0a9-e50e24dcca9e");

    [Fact]
    public void SuccessfulNegotiation_ReportsEveryStageThenReady()
    {
        RecordingObserver observer = new();
        GattNegotiationProgress progress = CreateProgress(observer);

        progress.Connecting();
        progress.Discovering();
        progress.DiscoverCharacteristics(supportsWriteWithoutResponse: true);
        progress.ConfigurationWritten();
        progress.PayloadNegotiated(attMtu: 247, payloadSize: 244);
        progress.Ready();

        Assert.Equal(
            [
                KissConnectionStage.ConnectingTransport,
                KissConnectionStage.DiscoveringServices,
                KissConnectionStage.WritingConfiguration,
                KissConnectionStage.NegotiatingPayload,
                KissConnectionStage.Ready
            ],
            observer.Stages);

        KissConnectionProgress ready = Assert.Single(observer.Reports, report => report.IsTerminal);
        Assert.Equal(KissConnectionOutcome.Succeeded, ready.Outcome);
        Assert.Equal(KissTransportKind.BluetoothLe, ready.Transport);
    }

    [Fact]
    public void Ready_ReportsTheNegotiatedGattDetail()
    {
        RecordingObserver observer = new();
        GattNegotiationProgress progress = CreateProgress(observer);

        progress.DiscoverCharacteristics(supportsWriteWithoutResponse: true);
        progress.ConfigurationWritten();
        progress.PayloadNegotiated(attMtu: 247, payloadSize: 244);
        progress.Ready();

        KissBluetoothLeDetail detail = Assert.Single(observer.Reports, report => report.IsTerminal).BluetoothLe
            ?? throw new InvalidOperationException("The ready report carried no GATT detail.");

        Assert.Equal(ServiceUuid, detail.ServiceUuid);
        Assert.Equal(RxUuid, detail.RxCharacteristicUuid);
        Assert.Equal(TxUuid, detail.TxCharacteristicUuid);
        Assert.True(detail.NotificationsEnabled);
        Assert.Equal(KissGattWriteMode.WithoutResponse, detail.WriteMode);
        Assert.Equal(247, detail.AttMtu);
        Assert.Equal(244, detail.PayloadSize);
    }

    [Fact]
    public void WriteModeReadFromProperties_DecidesTheChannelMode()
    {
        RecordingObserver observer = new();
        GattNegotiationProgress withResponse = CreateProgress(observer);
        GattNegotiationProgress withoutResponse = CreateProgress(new RecordingObserver());

        withResponse.DiscoverCharacteristics(supportsWriteWithoutResponse: false);
        withoutResponse.DiscoverCharacteristics(supportsWriteWithoutResponse: true);

        Assert.Equal(KissGattWriteMode.WithResponse, withResponse.WriteMode);
        Assert.Equal(KissGattWriteMode.WithoutResponse, withoutResponse.WriteMode);
    }

    [Fact]
    public void DiscoveryFailure_NamesTheServiceDiscoveryStage()
    {
        RecordingObserver observer = new();
        GattNegotiationProgress progress = CreateProgress(observer);

        progress.Discovering();
        progress.Failed(
            KissConnectionStage.DiscoveringServices,
            new IOException("BLE service discovery failed with status 133."));

        KissConnectionProgress failure = Assert.Single(observer.Reports, report => report.IsFailure);
        Assert.Equal(KissConnectionStage.DiscoveringServices, failure.Stage);
        Assert.Equal(KissConnectionOutcome.Failed, failure.Outcome);
        Assert.Contains("133", failure.Failure, StringComparison.Ordinal);
        Assert.True(progress.HasReportedFailure);
    }

    [Fact]
    public void CccdFailure_NamesTheConfigurationStage()
    {
        RecordingObserver observer = new();
        GattNegotiationProgress progress = CreateProgress(observer);
        progress.DiscoverCharacteristics(supportsWriteWithoutResponse: false);

        progress.Failed(KissConnectionStage.WritingConfiguration, "CCCD write failed.");

        Assert.Equal(
            KissConnectionStage.WritingConfiguration,
            Assert.Single(observer.Reports, report => report.IsFailure).Stage);
    }

    [Fact]
    public void FailedMidNegotiation_StillCarriesWhatWasAlreadyKnown()
    {
        RecordingObserver observer = new();
        GattNegotiationProgress progress = CreateProgress(observer);
        progress.DiscoverCharacteristics(supportsWriteWithoutResponse: true);
        progress.ConfigurationWritten();

        progress.Failed(KissConnectionStage.NegotiatingPayload, "MTU request failed.");

        KissBluetoothLeDetail detail = Assert.Single(observer.Reports, report => report.IsFailure).BluetoothLe
            ?? throw new InvalidOperationException("The failure report carried no GATT detail.");

        // The consumer can still see the write mode and notification state that were
        // established before the payload negotiation failed.
        Assert.Equal(KissGattWriteMode.WithoutResponse, detail.WriteMode);
        Assert.True(detail.NotificationsEnabled);
        Assert.Null(detail.AttMtu);
    }

    [Fact]
    public void CancelledAttempt_ReportsTheStageItWasWaitingIn()
    {
        RecordingObserver observer = new();
        GattNegotiationProgress progress = CreateProgress(observer);
        progress.Connecting();

        progress.Cancelled(KissConnectionStage.ConnectingTransport);

        KissConnectionProgress cancelled = Assert.Single(observer.Reports, report => report.IsTerminal);
        Assert.Equal(KissConnectionOutcome.Cancelled, cancelled.Outcome);
        Assert.Equal(KissConnectionStage.ConnectingTransport, cancelled.Stage);
    }

    [Fact]
    public void Reports_AreNumberedWithTheSessionAttempt()
    {
        RecordingObserver observer = new();
        GattNegotiationProgress progress = new(
            new KissConnectionContext(observer, Attempt: 3),
            ServiceUuid,
            RxUuid,
            TxUuid);

        progress.Connecting();
        progress.Ready();

        Assert.All(observer.Reports, report => Assert.Equal(3, report.Attempt));
    }

    private static GattNegotiationProgress CreateProgress(RecordingObserver observer) =>
        new(new KissConnectionContext(observer, Attempt: 1), ServiceUuid, RxUuid, TxUuid);

    private sealed class RecordingObserver : IKissConnectionObserver
    {
        public List<KissConnectionProgress> Reports { get; } = [];

        public List<KissConnectionStage> Stages { get; } = [];

        public void Report(KissConnectionProgress progress)
        {
            Reports.Add(progress);
            Stages.Add(progress.Stage);
        }
    }
}

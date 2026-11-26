// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Connects transport observations to the durable session coordinator.
/// </summary>
/// <remarks>
/// Transport events fire on modem read loops and must never block them or spawn one
/// task per packet. The bridge hands every observation to a bounded intake that a
/// single consumer drains, so a burst degrades into counted drops instead of
/// unbounded concurrency.
/// </remarks>
public sealed class SessionCoordinatorTelemetryBridge : IAsyncDisposable
{
    public const int DefaultObservationCapacity = 1024;

    private readonly IPortService _portService;
    private readonly IPortServiceTelemetry _telemetry;
    private readonly SessionCoordinator _coordinator;
    private readonly SessionTelemetryIntake _intake;
    private bool _disposed;

    public SessionCoordinatorTelemetryBridge(
        IPortService portService,
        SessionCoordinator coordinator,
        ILogger<SessionCoordinatorTelemetryBridge> logger,
        int observationCapacity = DefaultObservationCapacity)
    {
        ArgumentNullException.ThrowIfNull(portService);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(logger);

        _portService = portService;
        _coordinator = coordinator;
        _telemetry = portService as IPortServiceTelemetry
            ?? throw new ArgumentException("The port service must expose transport telemetry.", nameof(portService));
        _intake = new SessionTelemetryIntake(HandleAsync, logger, observationCapacity);

        _portService.PacketReceived += OnPacketReceived;
        _telemetry.PacketTransmitted += OnPacketTransmitted;
        _telemetry.TransportFaulted += OnTransportFaulted;
    }

    /// <summary>
    /// Observations discarded because the intake queue was full.
    /// </summary>
    public long DroppedObservations => _intake.DroppedObservations;

    /// <summary>
    /// Completes once every observation raised before this call has been applied to
    /// the coordinator.
    /// </summary>
    internal Task FlushAsync(CancellationToken cancellationToken = default) =>
        _intake.FlushAsync(cancellationToken);

    private void OnPacketReceived(object? sender, PortPacketReceivedEventArgs args) =>
        _intake.Post(new SessionTelemetryObservation(
            args.PortId,
            SessionTelemetryKind.Receive,
            Error: null));

    private void OnPacketTransmitted(object? sender, PortPacketTransmittedEventArgs args) =>
        _intake.Post(new SessionTelemetryObservation(
            args.PortId,
            SessionTelemetryKind.Transmit,
            Error: null));

    private void OnTransportFaulted(object? sender, PortTransportFaultEventArgs args) =>
        _intake.Post(new SessionTelemetryObservation(
            args.PortId,
            SessionTelemetryKind.Fault,
            args.Exception));

    private Task HandleAsync(SessionTelemetryObservation observation) =>
        observation.Kind switch
        {
            SessionTelemetryKind.Receive => _coordinator.ReportReceiveAsync(observation.PortId),
            SessionTelemetryKind.Transmit => _coordinator.ReportTransmitAsync(observation.PortId),
            SessionTelemetryKind.Fault when observation.Error is { } error =>
                _coordinator.ReportPortFaultAsync(observation.PortId, error),
            _ => Task.CompletedTask
        };

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _portService.PacketReceived -= OnPacketReceived;
        _telemetry.PacketTransmitted -= OnPacketTransmitted;
        _telemetry.TransportFaulted -= OnTransportFaulted;
        await _intake.DisposeAsync().ConfigureAwait(false);
    }
}

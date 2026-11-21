// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;
using AetherAprs.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Ports;

/// <summary>
/// Connects transport observations to the durable session coordinator.
/// </summary>
public sealed class SessionCoordinatorTelemetryBridge : IDisposable
{
    private readonly IPortService _portService;
    private readonly IPortServiceTelemetry _telemetry;
    private readonly SessionCoordinator _coordinator;
    private readonly ILogger<SessionCoordinatorTelemetryBridge> _logger;
    private bool _disposed;

    public SessionCoordinatorTelemetryBridge(
        IPortService portService,
        SessionCoordinator coordinator,
        ILogger<SessionCoordinatorTelemetryBridge> logger)
    {
        _portService = portService;
        _telemetry = portService as IPortServiceTelemetry
            ?? throw new ArgumentException("The port service must expose transport telemetry.", nameof(portService));
        _coordinator = coordinator;
        _logger = logger;

        _portService.PacketReceived += OnPacketReceived;
        _telemetry.PacketTransmitted += OnPacketTransmitted;
        _telemetry.TransportFaulted += OnTransportFaulted;
    }

    private void OnPacketReceived(object? sender, PortPacketReceivedEventArgs args) =>
        ObserveAsync(
            () => _coordinator.ReportReceiveAsync(args.PortId),
            "receive",
            args.PortId);

    private void OnPacketTransmitted(object? sender, PortPacketTransmittedEventArgs args) =>
        ObserveAsync(
            () => _coordinator.ReportTransmitAsync(args.PortId),
            "transmit",
            args.PortId);

    private void OnTransportFaulted(object? sender, PortTransportFaultEventArgs args) =>
        ObserveAsync(
            () => _coordinator.ReportPortFaultAsync(args.PortId, args.Exception),
            "fault",
            args.PortId);

    private void ObserveAsync(Func<Task> operation, string observation, Guid portId)
    {
        _ = ObserveCoreAsync(operation, observation, portId);
    }

    private async Task ObserveCoreAsync(Func<Task> operation, string observation, Guid portId)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to record {Observation} telemetry for port {PortId}.",
                observation,
                portId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _portService.PacketReceived -= OnPacketReceived;
        _telemetry.PacketTransmitted -= OnPacketTransmitted;
        _telemetry.TransportFaulted -= OnTransportFaulted;
        _disposed = true;
    }
}

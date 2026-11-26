// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Diagnostics;

public sealed class RedactedSupportReportService(
    IDiagnosticEventSink diagnostics,
    SessionCoordinator coordinator,
    IPortServiceTelemetry? portTelemetry = null,
    AndroidLifecycleState? lifecycle = null,
    IKeepAliveService? keepAlive = null)
{
    private const int ReportSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string GenerateJson()
    {
        var snapshot = new
        {
            schemaVersion = ReportSchemaVersion,
            generatedAtUtc = DateTimeOffset.UtcNow,
            runtime = new
            {
                framework = Environment.Version.ToString(),
                os = Environment.OSVersion.Platform.ToString(),
                architecture = Environment.Is64BitProcess ? "x64" : "x86"
            },
            session = new
            {
                state = coordinator.State.ToString(),
                ports = coordinator.Ports.Select(port => new
                {
                    port.PortId,
                    state = port.State.ToString(),
                    port.LastReceiveUtc,
                    port.LastTransmitUtc,
                    port.RetryCount,
                    port.NextRetryUtc
                })
            },
            diagnostics = diagnostics.Events,
            lifecycle = lifecycle is null
                ? null
                : BuildLifecycle(lifecycle.Snapshot),
            keepAlive = keepAlive is null
                ? null
                : BuildKeepAlive(keepAlive.Status),
            packetIngest = portTelemetry is null
                ? null
                : new
                {
                    droppedPerPort = coordinator.Ports
                        .Select(port => new
                        {
                            port.PortId,
                            dropped = portTelemetry.GetDroppedPacketCount(port.PortId),
                            droppedForwards = portTelemetry.GetDroppedForwardCount(port.PortId)
                        })
                        .ToArray()
                }
        };

        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private static object BuildLifecycle(AndroidLifecycleSnapshot snapshot) => new
    {
        processAlive = snapshot.ProcessAlive,
        activityState = snapshot.ActivityState.ToString(),
        foregroundState = snapshot.ForegroundState.ToString(),
        transportState = snapshot.TransportState.ToString(),
        notificationPosted = snapshot.NotificationPosted,
        permissions = snapshot.Permissions
            .OrderBy(entry => entry.Key)
            .Select(entry => new
            {
                name = entry.Key.ToString(),
                granted = entry.Value
            })
            .ToArray()
    };

    private static object BuildKeepAlive(KeepAliveStatus status) => new
    {
        state = status.State.ToString(),
        reasons = status.Reasons
            .Select(reason => reason.ToString())
            .ToArray(),
        requiredTypes = status.RequiredTypes.ToString(),
        degradedReason = status.DegradedReason
    };

    public async Task WriteAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, GenerateJson(), cancellationToken)
            .ConfigureAwait(false);
        File.Move(temporaryPath, path, overwrite: true);
    }
}

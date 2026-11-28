// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;

namespace AetherAprs.Services.Diagnostics;

/// <summary>
/// Builds the redacted support report as a JSON document.
/// </summary>
/// <remarks>
/// The report is a diagnostic shape assembled at runtime, so it is written as an
/// explicit <see cref="JsonObject"/> graph instead of serializing anonymous
/// types. That keeps the report working in a trimmed application, where
/// reflection-based serialization has no metadata to work from.
/// </remarks>
public sealed class RedactedSupportReportService(
    IDiagnosticEventSink diagnostics,
    SessionCoordinator coordinator,
    IPortServiceTelemetry? portTelemetry = null,
    AndroidLifecycleState? lifecycle = null,
    IKeepAliveService? keepAlive = null) : ISupportReportBuilder
{
    private const int ReportSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Builds the report and returns it as indented JSON.
    /// </summary>
    /// <returns>The redacted support report.</returns>
    public string GenerateJson()
    {
        IReadOnlyList<PortSessionStatus> ports = coordinator.Ports;

        JsonObject report = new()
        {
            ["schemaVersion"] = ReportSchemaVersion,
            ["generatedAtUtc"] = JsonValue.Create(DateTimeOffset.UtcNow),
            ["runtime"] = new JsonObject
            {
                ["framework"] = Environment.Version.ToString(),
                ["os"] = Environment.OSVersion.Platform.ToString(),
                ["architecture"] = Environment.Is64BitProcess ? "x64" : "x86"
            },
            ["session"] = new JsonObject
            {
                ["state"] = coordinator.State.ToString(),
                ["ports"] = ToArray(ports, BuildPort)
            },
            ["diagnostics"] = ToArray(diagnostics.Events, BuildDiagnostic),
            ["lifecycle"] = lifecycle is null
                ? null
                : BuildLifecycle(lifecycle.Snapshot),
            ["keepAlive"] = keepAlive is null
                ? null
                : BuildKeepAlive(keepAlive.Status),
            ["packetIngest"] = portTelemetry is null
                ? null
                : BuildPacketIngest(portTelemetry, ports)
        };

        return report.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// Writes the report to disk, replacing the file atomically.
    /// </summary>
    /// <param name="path">The destination path.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task WriteAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, GenerateJson(), cancellationToken)
            .ConfigureAwait(false);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static JsonArray ToArray<T>(IEnumerable<T> items, Func<T, JsonNode?> map) =>
        [with([.. items.Select(map)])];

    private static JsonObject BuildPort(PortSessionStatus port) => new()
    {
        ["PortId"] = port.PortId,
        ["state"] = port.State.ToString(),
        ["LastReceiveUtc"] = Optional(port.LastReceiveUtc),
        ["LastTransmitUtc"] = Optional(port.LastTransmitUtc),
        ["RetryCount"] = port.RetryCount,
        ["NextRetryUtc"] = Optional(port.NextRetryUtc)
    };

    private static JsonObject BuildDiagnostic(DiagnosticEvent diagnostic) => new()
    {
        ["Id"] = diagnostic.Id,
        ["TimestampUtc"] = JsonValue.Create(diagnostic.TimestampUtc),
        ["Category"] = diagnostic.Category,

        // Severity stays numeric so the report keeps the shape operators and
        // tooling already read.
        ["Severity"] = (int)diagnostic.Severity,
        ["State"] = diagnostic.State,
        ["Reason"] = diagnostic.Reason,
        ["SafeIdentifier"] = Optional(diagnostic.SafeIdentifier)
    };

    private static JsonObject BuildLifecycle(AndroidLifecycleSnapshot snapshot) => new()
    {
        ["processAlive"] = snapshot.ProcessAlive,
        ["activityState"] = snapshot.ActivityState.ToString(),
        ["foregroundState"] = snapshot.ForegroundState.ToString(),
        ["transportState"] = snapshot.TransportState.ToString(),
        ["notificationPosted"] = snapshot.NotificationPosted,
        ["permissions"] = ToArray(
            snapshot.Permissions.OrderBy(entry => entry.Key),
            entry => new JsonObject
            {
                ["name"] = entry.Key.ToString(),
                ["granted"] = entry.Value
            })
    };

    private static JsonObject BuildKeepAlive(KeepAliveStatus status) => new()
    {
        ["state"] = status.State.ToString(),
        ["reasons"] = new JsonArray([.. status.Reasons.Select(reason => (JsonNode?)reason.ToString())]),
        ["requiredTypes"] = status.RequiredTypes.ToString(),
        ["degradedReason"] = Optional(status.DegradedReason)
    };

    private static JsonObject BuildPacketIngest(
        IPortServiceTelemetry telemetry,
        IReadOnlyList<PortSessionStatus> ports) => new()
        {
            ["droppedPerPort"] = ToArray(ports, port => new JsonObject
            {
                ["PortId"] = port.PortId,
                ["dropped"] = telemetry.GetDroppedPacketCount(port.PortId),
                ["droppedForwards"] = telemetry.GetDroppedForwardCount(port.PortId)
            })
        };

    private static JsonNode? Optional(DateTimeOffset? value) =>
        value is { } present ? present : null;

    private static JsonNode? Optional(string? value) =>
        value is null ? null : JsonValue.Create(value);
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Services.Diagnostics;

public sealed class DiagnosticsTests
{
    [Fact]
    public void EventStore_IsBounded_AndRedactsSensitiveValues()
    {
        var store = new BoundedDiagnosticEventStore(maximumEvents: 2);

        store.Record(
            "transport",
            DiagnosticSeverity.Error,
            "Faulted",
            "passcode=secret123 N0CALL>APRS:private message");
        store.Record("session", DiagnosticSeverity.Information, "Running", "safe");
        store.Record("session", DiagnosticSeverity.Warning, "Degraded", "latest");

        var events = store.Events;
        Assert.Equal(2, events.Count);
        Assert.DoesNotContain(events, item => item.Reason.Contains("secret123", StringComparison.Ordinal));
        Assert.DoesNotContain(events, item => item.Reason.Contains("private message", StringComparison.Ordinal));
        Assert.Equal("latest", events[^1].Reason);
    }

    [Fact]
    public async Task SupportReport_ExcludesCredentialsPacketHistoryAndMessageText()
    {
        var portId = Guid.NewGuid();
        var store = new BoundedDiagnosticEventStore();
        store.Record(
            "transport",
            DiagnosticSeverity.Error,
            "Faulted",
            "password=topsecret N0CALL>APRS:do not export");

        await using var coordinator = new SessionCoordinator(
            new FakePortController(),
            new FakeStateStore());
        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync([portId], TestContext.Current.CancellationToken);

        var report = new RedactedSupportReportService(store, coordinator).GenerateJson();

        Assert.Contains("\"schemaVersion\": 1", report, StringComparison.Ordinal);
        Assert.Contains("Faulted", report, StringComparison.Ordinal);
        Assert.DoesNotContain("topsecret", report, StringComparison.Ordinal);
        Assert.DoesNotContain("do not export", report, StringComparison.Ordinal);
        Assert.DoesNotContain("N0CALL>APRS", report, StringComparison.Ordinal);
        Assert.DoesNotContain("passcode", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SupportReport_IncludesAndroidLifecycleSnapshot_WithoutSensitiveValues()
    {
        var store = new BoundedDiagnosticEventStore();
        var lifecycle = new AndroidLifecycleState();
        lifecycle.ProcessStarted();
        lifecycle.ActivityCreated();
        lifecycle.ActivityResumed();
        lifecycle.ForegroundStarted();
        lifecycle.TransportConnected();
        lifecycle.NotificationPosted();
        lifecycle.PermissionResult(AndroidPermission.Location, granted: true);
        lifecycle.PermissionResult(AndroidPermission.Notifications, granted: false);

        await using var coordinator = new SessionCoordinator(
            new FakePortController(),
            new FakeStateStore());
        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        var report = new RedactedSupportReportService(
            store,
            coordinator,
            portTelemetry: null,
            lifecycle: lifecycle).GenerateJson();

        Assert.Contains("\"processAlive\": true", report, StringComparison.Ordinal);
        Assert.Contains("\"activityState\": \"Resumed\"", report, StringComparison.Ordinal);
        Assert.Contains("\"foregroundState\": \"Running\"", report, StringComparison.Ordinal);
        Assert.Contains("\"transportState\": \"Connected\"", report, StringComparison.Ordinal);
        Assert.Contains("\"notificationPosted\": true", report, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"Location\"", report, StringComparison.Ordinal);
        Assert.Contains("\"granted\": false", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SupportReport_OmitsLifecycleSection_WhenStateIsNotProvided()
    {
        await using var coordinator = new SessionCoordinator(
            new FakePortController(),
            new FakeStateStore());

        var report = new RedactedSupportReportService(
            new BoundedDiagnosticEventStore(),
            coordinator).GenerateJson();

        Assert.Contains("\"lifecycle\": null", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SupportReport_IncludesTheKeepAliveHoldState()
    {
        using var keepAlive = new KeepAliveService(
            new StubForegroundService(),
            NullLogger<KeepAliveService>.Instance);
        await keepAlive.AcquireAsync(KeepAliveReason.LocationTracking);

        await using var coordinator = new SessionCoordinator(
            new FakePortController(),
            new FakeStateStore());

        var report = new RedactedSupportReportService(
            new BoundedDiagnosticEventStore(),
            coordinator,
            keepAlive: keepAlive).GenerateJson();

        Assert.Contains("\"state\": \"Active\"", report, StringComparison.Ordinal);
        Assert.Contains("\"LocationTracking\"", report, StringComparison.Ordinal);
        Assert.Contains("\"requiredTypes\": \"Location\"", report, StringComparison.Ordinal);
        Assert.Contains("\"degradedReason\": null", report, StringComparison.Ordinal);
    }

    private sealed class StubForegroundService : IForegroundService
    {
        public bool IsRunning { get; private set; }

        public Task<ForegroundServiceStartResult> StartAsync(ForegroundServiceType requiredTypes)
        {
            IsRunning = true;
            return Task.FromResult(ForegroundServiceStartResult.Started());
        }

        public Task StopAsync()
        {
            IsRunning = false;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePortController : ISessionPortController
    {
        public Task ConnectAsync(Guid portId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DisconnectAsync(Guid portId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeStateStore : ISessionStateStore
    {
        public Task<SessionSnapshot?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<SessionSnapshot?>(null);

        public Task SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Ports;
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

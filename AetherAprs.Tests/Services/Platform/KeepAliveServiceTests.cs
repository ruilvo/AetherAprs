// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AetherAprs.Services.Diagnostics;
using AetherAprs.Services.Platform;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace AetherAprs.Tests.Services.Platform;

public sealed class KeepAliveServiceTests
{
    [Fact]
    public async Task AcquireAsync_FirstReason_StartsTheForegroundServiceAndReportsActive()
    {
        ScriptedForegroundService foreground = new();
        KeepAliveService service = CreateService(foreground);

        KeepAliveStatus status = await service.AcquireAsync(KeepAliveReason.Ports);

        Assert.Equal(KeepAliveState.Active, status.State);
        Assert.Equal([ForegroundServiceType.ConnectedDevice], foreground.StartRequests);
        Assert.Equal(0, foreground.StopCount);
        Assert.Same(status, service.Status);
    }

    [Fact]
    public async Task AcquireAsync_SameReasonTwice_StartsOnlyOnce()
    {
        ScriptedForegroundService foreground = new();
        KeepAliveService service = CreateService(foreground);

        await service.AcquireAsync(KeepAliveReason.Ports);
        await service.AcquireAsync(KeepAliveReason.Ports);

        Assert.Single(foreground.StartRequests);
        Assert.Equal(KeepAliveState.Active, service.Status.State);
    }

    [Fact]
    public async Task AcquireAsync_DistinctReasons_RefreshesDeclaredTypes()
    {
        ScriptedForegroundService foreground = new();
        KeepAliveService service = CreateService(foreground);

        await service.AcquireAsync(KeepAliveReason.Ports);
        KeepAliveStatus status = await service.AcquireAsync(KeepAliveReason.LocationTracking);

        Assert.Equal(
            [
                ForegroundServiceType.ConnectedDevice,
                ForegroundServiceType.ConnectedDevice | ForegroundServiceType.Location
            ],
            foreground.StartRequests);
        Assert.Equal(
            ForegroundServiceType.ConnectedDevice | ForegroundServiceType.Location,
            status.RequiredTypes);
        Assert.Equal(
            [KeepAliveReason.Ports, KeepAliveReason.LocationTracking],
            status.Reasons);
    }

    [Fact]
    public async Task AcquireAsync_WhenThePlatformRejectsTheStart_RollsBackTheReason()
    {
        ScriptedForegroundService foreground = new()
        {
            RejectionReason = "The platform refused the request."
        };
        KeepAliveService service = CreateService(foreground);
        List<KeepAliveState> states = RecordStates(service);

        KeepAliveStatus status = await service.AcquireAsync(KeepAliveReason.Ports);

        Assert.Equal(KeepAliveState.Degraded, status.State);
        Assert.Equal("The platform refused the request.", status.DegradedReason);
        Assert.Empty(status.Reasons);
        Assert.Equal(ForegroundServiceType.None, status.RequiredTypes);
        Assert.DoesNotContain(KeepAliveState.Active, states);
        Assert.Empty(service.Status.Reasons);
    }

    [Fact]
    public async Task AcquireAsync_WhenARefreshIsRejected_KeepsTheEarlierHoldRunning()
    {
        ScriptedForegroundService foreground = new();
        KeepAliveService service = CreateService(foreground);

        await service.AcquireAsync(KeepAliveReason.Ports);
        foreground.RejectionReason = "The platform refused the request.";

        KeepAliveStatus status = await service.AcquireAsync(KeepAliveReason.LocationTracking);

        // Only the refused reason is dropped: the hold that was already honoured
        // stays, and tearing the service down would be worse than the refusal.
        Assert.Equal(KeepAliveState.Degraded, status.State);
        Assert.Equal([KeepAliveReason.Ports], status.Reasons);
        Assert.Equal(ForegroundServiceType.ConnectedDevice, status.RequiredTypes);
        Assert.Equal(0, foreground.StopCount);
        Assert.True(foreground.IsRunning);
    }

    [Fact]
    public async Task ReleaseAsync_WhenTheTypeRefreshIsRejected_ReportsDegraded()
    {
        ScriptedForegroundService foreground = new();
        KeepAliveService service = CreateService(foreground);

        await service.AcquireAsync(KeepAliveReason.Ports);
        await service.AcquireAsync(KeepAliveReason.LocationTracking);
        foreground.RejectionReason = "The platform refused the request.";

        KeepAliveStatus status = await service.ReleaseAsync(KeepAliveReason.Ports);

        Assert.Equal(KeepAliveState.Degraded, status.State);
        Assert.Equal([KeepAliveReason.LocationTracking], status.Reasons);
        Assert.Equal("The platform refused the request.", status.DegradedReason);
        Assert.Equal(0, foreground.StopCount);
    }

    [Fact]
    public async Task AcquireAsync_AfterADegradedStart_RetriesTheHold()
    {
        ScriptedForegroundService foreground = new()
        {
            RejectionReason = "The platform refused the request."
        };
        KeepAliveService service = CreateService(foreground);

        await service.AcquireAsync(KeepAliveReason.Ports);
        foreground.RejectionReason = null;
        KeepAliveStatus status = await service.AcquireAsync(KeepAliveReason.Ports);

        Assert.Equal(KeepAliveState.Active, status.State);
        Assert.Equal(2, foreground.StartRequests.Count);
    }

    [Fact]
    public async Task AcquireAsync_WhenThePlatformThrows_ReportsDegradedWithTheFailure()
    {
        ScriptedForegroundService foreground = new()
        {
            StartException = new InvalidOperationException("binder failed")
        };
        KeepAliveService service = CreateService(foreground);

        KeepAliveStatus status = await service.AcquireAsync(KeepAliveReason.LocationTracking);

        Assert.Equal(KeepAliveState.Degraded, status.State);
        Assert.Contains("binder failed", status.DegradedReason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleaseAsync_WhileAnotherReasonIsHeld_KeepsTheServiceRunning()
    {
        ScriptedForegroundService foreground = new();
        KeepAliveService service = CreateService(foreground);

        await service.AcquireAsync(KeepAliveReason.Ports);
        await service.AcquireAsync(KeepAliveReason.LocationTracking);
        foreground.StartRequests.Clear();

        KeepAliveStatus status = await service.ReleaseAsync(KeepAliveReason.Ports);

        Assert.Equal(KeepAliveState.Active, status.State);
        Assert.Equal(0, foreground.StopCount);

        // Dropping a reason narrows the declared types, so the platform is told.
        Assert.Equal([ForegroundServiceType.Location], foreground.StartRequests);
    }

    [Fact]
    public async Task ReleaseAsync_LastReason_ReportsStoppingThenStopped()
    {
        ScriptedForegroundService foreground = new();
        KeepAliveService service = CreateService(foreground);
        List<KeepAliveState> states = RecordStates(service);

        await service.AcquireAsync(KeepAliveReason.Ports);
        KeepAliveStatus status = await service.ReleaseAsync(KeepAliveReason.Ports);

        Assert.Equal(
            [KeepAliveState.Active, KeepAliveState.Stopping, KeepAliveState.Stopped],
            states);
        Assert.Equal(KeepAliveState.Stopped, status.State);
        Assert.Equal(1, foreground.StopCount);
        Assert.False(foreground.IsRunning);
    }

    [Fact]
    public async Task ReleaseAsync_WithoutAcquire_DoesNotTouchThePlatform()
    {
        ScriptedForegroundService foreground = new();
        KeepAliveService service = CreateService(foreground);

        KeepAliveStatus status = await service.ReleaseAsync(KeepAliveReason.Ports);

        Assert.Equal(KeepAliveState.Stopped, status.State);
        Assert.Empty(foreground.StartRequests);
        Assert.Equal(0, foreground.StopCount);
    }

    [Fact]
    public async Task ReleaseAsync_WhenThePlatformKeepsRunning_ReportsDegraded()
    {
        ScriptedForegroundService foreground = new();
        KeepAliveService service = CreateService(foreground);

        await service.AcquireAsync(KeepAliveReason.Ports);
        foreground.KeepsRunningAfterStop = true;

        KeepAliveStatus status = await service.ReleaseAsync(KeepAliveReason.Ports);

        Assert.Equal(KeepAliveState.Degraded, status.State);
        Assert.Empty(status.Reasons);
        Assert.NotNull(status.DegradedReason);
    }

    [Fact]
    public async Task ConcurrentAcquireAndRelease_ApplyInOrderAndConverge()
    {
        TaskCompletionSource bridge = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedForegroundService foreground = new() { StartGate = bridge };
        KeepAliveService service = CreateService(foreground);

        Task<KeepAliveStatus> acquire = service.AcquireAsync(KeepAliveReason.Ports);
        await foreground.StartEntered.Task.WaitAsync(
            TestTimeouts.TestTimeout,
            TestContext.Current.CancellationToken);

        // The release arrives while the start is still in flight. It must wait,
        // never race the start with a stop.
        Task<KeepAliveStatus> release = service.ReleaseAsync(KeepAliveReason.Ports);
        Assert.False(release.IsCompleted);

        bridge.SetResult();
        await acquire.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);
        await release.WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.False(foreground.StopAttemptedWhileStarting);
        Assert.Single(foreground.StartRequests);
        Assert.Equal(1, foreground.StopCount);
        Assert.Equal(KeepAliveState.Stopped, service.Status.State);
        Assert.False(foreground.IsRunning);
    }

    [Fact]
    public async Task ConcurrentAcquiresForDistinctReasons_HoldEveryReasonOnce()
    {
        ScriptedForegroundService foreground = new();
        KeepAliveService service = CreateService(foreground);

        await Task.WhenAll(
                service.AcquireAsync(KeepAliveReason.Ports),
                service.AcquireAsync(KeepAliveReason.LocationTracking),
                service.AcquireAsync(KeepAliveReason.Ports))
            .WaitAsync(TestTimeouts.TestTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(KeepAliveState.Active, service.Status.State);
        Assert.Equal(
            [KeepAliveReason.Ports, KeepAliveReason.LocationTracking],
            service.Status.Reasons);
        Assert.Equal(
            ForegroundServiceType.ConnectedDevice | ForegroundServiceType.Location,
            service.Status.RequiredTypes);

        // Whichever reason arrives second refreshes the declared types, and the
        // duplicate acquire after that finds the hold already correct.
        Assert.Equal(
            ForegroundServiceType.ConnectedDevice | ForegroundServiceType.Location,
            foreground.StartRequests[^1]);
    }

    [Fact]
    public async Task DegradedTransition_IsRecordedInDiagnostics()
    {
        ScriptedForegroundService foreground = new() { RejectionReason = "refused by policy" };
        BoundedDiagnosticEventStore diagnostics = new();
        KeepAliveService service = new(
            foreground,
            NullLogger<KeepAliveService>.Instance,
            diagnostics);

        await service.AcquireAsync(KeepAliveReason.Ports);

        DiagnosticEvent recorded = Assert.Single(diagnostics.Events);
        Assert.Equal("keepalive", recorded.Category, ignoreCase: true);
        Assert.Equal(DiagnosticSeverity.Warning, recorded.Severity);
        Assert.Equal(nameof(KeepAliveState.Degraded), recorded.State);
        Assert.Contains("refused by policy", recorded.Reason, StringComparison.Ordinal);
    }

    private static KeepAliveService CreateService(IForegroundService foreground) =>
        new(foreground, NullLogger<KeepAliveService>.Instance);

    private static List<KeepAliveState> RecordStates(IKeepAliveService service)
    {
        List<KeepAliveState> states = [];
        service.StatusChanged += (_, args) => states.Add(args.Status.State);
        return states;
    }

    /// <summary>
    /// Foreground service double that can reject, throw, block a start, and keep
    /// running after a stop, so each degraded path is reachable.
    /// </summary>
    private sealed class ScriptedForegroundService : IForegroundService
    {
        public List<ForegroundServiceType> StartRequests { get; } = [];

        public int StopCount { get; private set; }

        public bool IsRunning { get; set; }

        public bool KeepsRunningAfterStop { get; set; }

        public string? RejectionReason { get; set; }

        public Exception? StartException { get; set; }

        public TaskCompletionSource? StartGate { get; set; }

        public TaskCompletionSource StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool StopAttemptedWhileStarting { get; private set; }

        public async Task<ForegroundServiceStartResult> StartAsync(ForegroundServiceType requiredTypes)
        {
            StartRequests.Add(requiredTypes);
            StartEntered.TrySetResult();

            if (StartGate is not null)
            {
                await StartGate.Task.ConfigureAwait(false);
            }

            if (StartException is not null)
            {
                throw StartException;
            }

            if (RejectionReason is not null)
            {
                return ForegroundServiceStartResult.Rejected(RejectionReason);
            }

            IsRunning = true;
            return ForegroundServiceStartResult.Started();
        }

        public Task StopAsync()
        {
            StopCount++;

            if (StartGate is { Task.IsCompleted: false })
            {
                StopAttemptedWhileStarting = true;
            }

            if (!KeepsRunningAfterStop)
            {
                IsRunning = false;
            }

            return Task.CompletedTask;
        }
    }
}

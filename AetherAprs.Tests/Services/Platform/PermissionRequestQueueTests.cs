// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using AetherAprs.Services.Platform;

using Xunit;

namespace AetherAprs.Tests.Services.Platform;

public sealed class PermissionRequestQueueTests
{
    [Fact]
    public async Task RequestAsync_ReturnsTheResultOfItsOwnRequest()
    {
        RecordingPromptHost host = new();
        using PermissionRequestQueue queue = new(host);

        Task<PermissionPromptResult> request = queue.RequestAsync(PermissionKind.Notifications, TestContext.Current.CancellationToken);
        (PermissionKind Kind, int RequestId) = await host.NextShownAsync();

        Assert.Equal(PermissionKind.Notifications, Kind);
        Assert.True(queue.Complete(RequestId, granted: true));
        Assert.Equal(PermissionPromptResult.Granted, await request);
        Assert.Null(queue.PendingRequestId);
    }

    [Fact]
    public async Task RequestAsync_WhenALateResultForAnEarlierRequestArrives_DoesNotSettleThePendingOne()
    {
        RecordingPromptHost host = new();
        using PermissionRequestQueue queue = new(host);

        Task<PermissionPromptResult> first = queue.RequestAsync(PermissionKind.Notifications, TestContext.Current.CancellationToken);
        (PermissionKind Kind, int RequestId) = await host.NextShownAsync();
        queue.Complete(RequestId, granted: false);
        Assert.Equal(PermissionPromptResult.Denied, await first);

        Task<PermissionPromptResult> second = queue.RequestAsync(PermissionKind.Audio, TestContext.Current.CancellationToken);
        (PermissionKind Kind, int RequestId) secondShown = await host.NextShownAsync();
        Assert.NotEqual(RequestId, secondShown.RequestId);

        // The dialog for the earlier request is gone, but a delayed duplicate
        // answer must not grant the prompt that is on screen now.
        Assert.False(queue.Complete(RequestId, granted: true));
        Assert.False(second.IsCompleted);
        Assert.Equal(secondShown.RequestId, queue.PendingRequestId);

        Assert.True(queue.Complete(secondShown.RequestId, granted: false));
        Assert.Equal(PermissionPromptResult.Denied, await second);
    }

    [Fact]
    public async Task RequestAsync_ShowsOnePromptAtATime()
    {
        RecordingPromptHost host = new();
        using PermissionRequestQueue queue = new(host);

        Task<PermissionPromptResult> first = queue.RequestAsync(PermissionKind.Notifications, TestContext.Current.CancellationToken);
        (PermissionKind Kind, int RequestId) = await host.NextShownAsync();

        // The second call cannot reach the host while the first dialog is open.
        Task<PermissionPromptResult> second = queue.RequestAsync(PermissionKind.Bluetooth, TestContext.Current.CancellationToken);
        Assert.Equal(RequestId, queue.PendingRequestId);
        Assert.Single(host.Shown);

        queue.Complete(RequestId, granted: true);
        Assert.Equal(PermissionPromptResult.Granted, await first);

        (PermissionKind Kind, int RequestId) secondShown = await host.NextShownAsync();
        Assert.Equal(PermissionKind.Bluetooth, secondShown.Kind);
        Assert.True(secondShown.RequestId > RequestId);

        queue.Complete(secondShown.RequestId, granted: false);
        Assert.Equal(PermissionPromptResult.Denied, await second);
        Assert.Equal(2, host.Shown.Count);
    }

    [Fact]
    public async Task Close_AbandonsThePendingRequest()
    {
        RecordingPromptHost host = new();
        using PermissionRequestQueue queue = new(host);

        Task<PermissionPromptResult> request = queue.RequestAsync(PermissionKind.Bluetooth, TestContext.Current.CancellationToken);
        (PermissionKind Kind, int RequestId) = await host.NextShownAsync();

        queue.Close();

        Assert.Equal(PermissionPromptResult.Abandoned, await request);
        Assert.Null(queue.PendingRequestId);
        Assert.False(queue.Complete(RequestId, granted: true));
    }

    [Fact]
    public async Task Close_ThenALaterRequest_IsAbandonedWithoutPrompting()
    {
        RecordingPromptHost host = new();
        using PermissionRequestQueue queue = new(host);

        queue.Close();

        PermissionPromptResult result = await queue.RequestAsync(PermissionKind.Notifications, TestContext.Current.CancellationToken);

        Assert.Equal(PermissionPromptResult.Abandoned, result);
        Assert.Empty(host.Shown);
    }

    [Fact]
    public void Complete_ForARequestThatWasNeverIssued_IsIgnored()
    {
        RecordingPromptHost host = new();
        using PermissionRequestQueue queue = new(host);

        Assert.False(queue.Complete(7, granted: true));
        Assert.Null(queue.PendingRequestId);
    }

    [Fact]
    public async Task RequestAsync_WhenTheWaitIsCancelled_ThrowsAndLeavesNothingPending()
    {
        RecordingPromptHost host = new();
        using PermissionRequestQueue queue = new(host);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task<PermissionPromptResult> request = queue.RequestAsync(PermissionKind.Audio, cancellation.Token);
        (PermissionKind Kind, int RequestId) = await host.NextShownAsync();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Null(queue.PendingRequestId);
        Assert.False(queue.Complete(RequestId, granted: true));
    }

    [Fact]
    public async Task RequestAsync_WhenThePromptCannotBeShown_ReportsAbandoned()
    {
        using PermissionRequestQueue queue = new(new ThrowingPromptHost());

        PermissionPromptResult result = await queue.RequestAsync(PermissionKind.Notifications, TestContext.Current.CancellationToken);

        Assert.Equal(PermissionPromptResult.Abandoned, result);
        Assert.Null(queue.PendingRequestId);
    }

    [Fact]
    public async Task RequestAsync_AfterDispose_Throws()
    {
        PermissionRequestQueue queue = new(new RecordingPromptHost());
        queue.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => queue.RequestAsync(PermissionKind.Notifications, TestContext.Current.CancellationToken));
    }

    private sealed class RecordingPromptHost : IPermissionPromptHost
    {
        private readonly Channel<(PermissionKind Kind, int RequestId)> _shown =
            Channel.CreateUnbounded<(PermissionKind Kind, int RequestId)>();

        private readonly List<(PermissionKind Kind, int RequestId)> _all = [];

        public IReadOnlyList<(PermissionKind Kind, int RequestId)> Shown
        {
            get
            {
                lock (_all)
                {
                    return [.. _all];
                }
            }
        }

        public void Show(PermissionKind kind, int requestId)
        {
            lock (_all)
            {
                _all.Add((kind, requestId));
            }

            _shown.Writer.TryWrite((kind, requestId));
        }

        public async Task<(PermissionKind Kind, int RequestId)> NextShownAsync()
        {
            Task<(PermissionKind Kind, int RequestId)> reading = _shown.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask();
            return await reading.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    private sealed class ThrowingPromptHost : IPermissionPromptHost
    {
        public void Show(PermissionKind kind, int requestId) =>
            throw new InvalidOperationException("The activity is gone.");
    }
}

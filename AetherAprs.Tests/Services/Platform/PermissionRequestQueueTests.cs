// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Services.Platform;

public sealed class PermissionRequestQueueTests
{
    [Fact]
    public async Task RequestAsync_ReturnsTheResultOfItsOwnRequest()
    {
        var host = new RecordingPromptHost();
        using var queue = new PermissionRequestQueue(host);

        var request = queue.RequestAsync(PermissionKind.Notifications, TestContext.Current.CancellationToken);
        var shown = await host.NextShownAsync();

        Assert.Equal(PermissionKind.Notifications, shown.Kind);
        Assert.True(queue.Complete(shown.RequestId, granted: true));
        Assert.Equal(PermissionPromptResult.Granted, await request);
        Assert.Null(queue.PendingRequestId);
    }

    [Fact]
    public async Task RequestAsync_WhenALateResultForAnEarlierRequestArrives_DoesNotSettleThePendingOne()
    {
        var host = new RecordingPromptHost();
        using var queue = new PermissionRequestQueue(host);

        var first = queue.RequestAsync(PermissionKind.Notifications, TestContext.Current.CancellationToken);
        var firstShown = await host.NextShownAsync();
        queue.Complete(firstShown.RequestId, granted: false);
        Assert.Equal(PermissionPromptResult.Denied, await first);

        var second = queue.RequestAsync(PermissionKind.Audio, TestContext.Current.CancellationToken);
        var secondShown = await host.NextShownAsync();
        Assert.NotEqual(firstShown.RequestId, secondShown.RequestId);

        // The dialog for the earlier request is gone, but a delayed duplicate
        // answer must not grant the prompt that is on screen now.
        Assert.False(queue.Complete(firstShown.RequestId, granted: true));
        Assert.False(second.IsCompleted);
        Assert.Equal(secondShown.RequestId, queue.PendingRequestId);

        Assert.True(queue.Complete(secondShown.RequestId, granted: false));
        Assert.Equal(PermissionPromptResult.Denied, await second);
    }

    [Fact]
    public async Task RequestAsync_ShowsOnePromptAtATime()
    {
        var host = new RecordingPromptHost();
        using var queue = new PermissionRequestQueue(host);

        var first = queue.RequestAsync(PermissionKind.Notifications, TestContext.Current.CancellationToken);
        var firstShown = await host.NextShownAsync();

        // The second call cannot reach the host while the first dialog is open.
        var second = queue.RequestAsync(PermissionKind.Bluetooth, TestContext.Current.CancellationToken);
        Assert.Equal(firstShown.RequestId, queue.PendingRequestId);
        Assert.Single(host.Shown);

        queue.Complete(firstShown.RequestId, granted: true);
        Assert.Equal(PermissionPromptResult.Granted, await first);

        var secondShown = await host.NextShownAsync();
        Assert.Equal(PermissionKind.Bluetooth, secondShown.Kind);
        Assert.True(secondShown.RequestId > firstShown.RequestId);

        queue.Complete(secondShown.RequestId, granted: false);
        Assert.Equal(PermissionPromptResult.Denied, await second);
        Assert.Equal(2, host.Shown.Count);
    }

    [Fact]
    public async Task Close_AbandonsThePendingRequest()
    {
        var host = new RecordingPromptHost();
        using var queue = new PermissionRequestQueue(host);

        var request = queue.RequestAsync(PermissionKind.Bluetooth, TestContext.Current.CancellationToken);
        var shown = await host.NextShownAsync();

        queue.Close();

        Assert.Equal(PermissionPromptResult.Abandoned, await request);
        Assert.Null(queue.PendingRequestId);
        Assert.False(queue.Complete(shown.RequestId, granted: true));
    }

    [Fact]
    public async Task Close_ThenALaterRequest_IsAbandonedWithoutPrompting()
    {
        var host = new RecordingPromptHost();
        using var queue = new PermissionRequestQueue(host);

        queue.Close();

        var result = await queue.RequestAsync(PermissionKind.Notifications, TestContext.Current.CancellationToken);

        Assert.Equal(PermissionPromptResult.Abandoned, result);
        Assert.Empty(host.Shown);
    }

    [Fact]
    public void Complete_ForARequestThatWasNeverIssued_IsIgnored()
    {
        var host = new RecordingPromptHost();
        using var queue = new PermissionRequestQueue(host);

        Assert.False(queue.Complete(7, granted: true));
        Assert.Null(queue.PendingRequestId);
    }

    [Fact]
    public async Task RequestAsync_WhenTheWaitIsCancelled_ThrowsAndLeavesNothingPending()
    {
        var host = new RecordingPromptHost();
        using var queue = new PermissionRequestQueue(host);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var request = queue.RequestAsync(PermissionKind.Audio, cancellation.Token);
        var shown = await host.NextShownAsync();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Null(queue.PendingRequestId);
        Assert.False(queue.Complete(shown.RequestId, granted: true));
    }

    [Fact]
    public async Task RequestAsync_WhenThePromptCannotBeShown_ReportsAbandoned()
    {
        using var queue = new PermissionRequestQueue(new ThrowingPromptHost());

        var result = await queue.RequestAsync(PermissionKind.Notifications, TestContext.Current.CancellationToken);

        Assert.Equal(PermissionPromptResult.Abandoned, result);
        Assert.Null(queue.PendingRequestId);
    }

    [Fact]
    public async Task RequestAsync_AfterDispose_Throws()
    {
        var queue = new PermissionRequestQueue(new RecordingPromptHost());
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
                    return _all.ToArray();
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
            var reading = _shown.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask();
            return await reading.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    private sealed class ThrowingPromptHost : IPermissionPromptHost
    {
        public void Show(PermissionKind kind, int requestId) =>
            throw new InvalidOperationException("The activity is gone.");
    }
}

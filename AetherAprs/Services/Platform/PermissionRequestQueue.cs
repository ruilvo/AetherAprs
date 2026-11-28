// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace AetherAprs.Services.Platform;

/// <summary>
/// Serializes permission prompts and matches each answer to the request that
/// asked for it.
/// </summary>
/// <remarks>
/// <para>
/// The platform shows one permission dialog at a time and answers it on a global
/// callback, so overlapping prompts would let one answer settle the wrong waiter.
/// Requests are therefore queued: a second request waits until the first prompt
/// has been answered. Every request gets an id, and
/// <see cref="Complete"/> only settles the request that owns that id. A late
/// answer for a prompt the operator already dismissed, or one that belongs to a
/// destroyed activity, is ignored instead of granting whatever happens to be
/// waiting now.
/// </para>
/// <para>
/// <see cref="Close"/> is the teardown path. A pending request completes as
/// <see cref="PermissionPromptResult.Abandoned"/>, and later requests are
/// abandoned without prompting, because an activity that is going away cannot
/// host a dialog. That outcome is deliberately not a denial: nothing should be
/// recorded about a choice the operator never made.
/// </para>
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="PermissionRequestQueue"/> class.
/// </remarks>
/// <param name="host">The object that shows the platform prompt.</param>
/// <param name="logger">An optional logger.</param>
public sealed class PermissionRequestQueue(
    IPermissionPromptHost host,
    ILogger<PermissionRequestQueue>? logger = null) : IDisposable
{
    private readonly IPermissionPromptHost _host = host ?? throw new ArgumentNullException(nameof(host));
    private readonly ILogger<PermissionRequestQueue>? _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private PendingRequest? _pending;
    private int _lastRequestId;
    private bool _closed;
    private bool _disposed;

    /// <summary>
    /// Gets the id of the request whose prompt is on screen, if any.
    /// </summary>
    public int? PendingRequestId => Volatile.Read(ref _pending)?.RequestId;

    /// <summary>
    /// Asks for <paramref name="kind"/>, waiting for the previous prompt to finish.
    /// </summary>
    /// <param name="kind">The permission to ask for.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The prompt result, or <see cref="PermissionPromptResult.Abandoned"/>.</returns>
    public async Task<PermissionPromptResult> RequestAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_closed)
            {
                return PermissionPromptResult.Abandoned;
            }

            cancellationToken.ThrowIfCancellationRequested();

            int requestId = Interlocked.Increment(ref _lastRequestId);
            PendingRequest pending = new(requestId);
            Volatile.Write(ref _pending, pending);

            try
            {
                _host.Show(kind, requestId);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "Showing the {Permission} prompt failed", kind);
                Interlocked.CompareExchange(ref _pending, null, pending);
                return PermissionPromptResult.Abandoned;
            }

            await using ConfiguredAsyncDisposable registration = cancellationToken.Register(
                () => pending.Completion.TrySetCanceled(cancellationToken)).ConfigureAwait(false);

            try
            {
                return await pending.Completion.Task.ConfigureAwait(false);
            }
            finally
            {
                Interlocked.CompareExchange(ref _pending, null, pending);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Settles the request that owns <paramref name="requestId"/>.
    /// </summary>
    /// <param name="requestId">The id the prompt was shown with.</param>
    /// <param name="granted">Whether the operator granted the permission.</param>
    /// <returns>
    /// <see langword="true"/> when the result settled the pending request;
    /// <see langword="false"/> when it was stale or nothing was pending.
    /// </returns>
    public bool Complete(int requestId, bool granted)
    {
        PendingRequest? pending = Volatile.Read(ref _pending);
        if (pending is null || pending.RequestId != requestId)
        {
            // A prompt answered after teardown, or two overlapping requests whose
            // answers arrived out of order; settling anything here would report a
            // choice the operator made for a different permission.
            _logger?.LogDebug(
                "Ignored a permission result for request {RequestId} because it is not the pending request",
                requestId);
            return false;
        }

        Interlocked.CompareExchange(ref _pending, null, pending);
        return pending.Completion.TrySetResult(
            granted ? PermissionPromptResult.Granted : PermissionPromptResult.Denied);
    }

    /// <summary>
    /// Stops accepting requests and abandons the one whose prompt is on screen.
    /// </summary>
    public void Close()
    {
        _closed = true;

        PendingRequest? pending = Volatile.Read(ref _pending);
        if (pending is null)
        {
            return;
        }

        Interlocked.CompareExchange(ref _pending, null, pending);
        pending.Completion.TrySetResult(PermissionPromptResult.Abandoned);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Close();
        _disposed = true;
        _gate.Dispose();
    }

    private sealed class PendingRequest(int requestId)
    {
        public int RequestId { get; } = requestId;

        public TaskCompletionSource<PermissionPromptResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

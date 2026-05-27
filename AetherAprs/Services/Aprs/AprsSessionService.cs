// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Aprs;

using AetherAprs.Messages;
using AetherAprs.Protocols;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class AprsSessionService : IAprsSessionService, IAsyncDisposable
{
    private readonly Dictionary<Guid, BackendEntry> _backends = new();
    private readonly object _gate = new();

    public AprsSessionService()
    {
        WeakReferenceMessenger.Default.Register<AprsSessionService, SendAprsPacketMessage>(this, static (service, message) =>
        {
            service.BroadcastAsync(message.Value);
        });
    }

    public async Task<Guid> RegisterBackendAsync(IAprsBackend backend, CancellationToken cancellationToken = default)
    {
        await backend.ConnectAsync(cancellationToken);

        var handle = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var task = RunReceiveLoopAsync(backend, cts.Token);

        lock (_gate)
        {
            _backends[handle] = new BackendEntry(backend, cts, task);
        }

        return handle;
    }

    public async Task UnregisterBackendAsync(Guid handle, CancellationToken cancellationToken = default)
    {
        BackendEntry? entry;
        lock (_gate)
        {
            if (!_backends.Remove(handle, out entry))
            {
                return;
            }
        }

        entry.Cts.Cancel();
        await entry.ReceiveTask.WaitAsync(cancellationToken);

        if (entry.Backend is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }

        entry.Cts.Dispose();
    }

    private void BroadcastAsync(Models.AprsPacket packet)
    {
        List<IAprsBackend> snapshot;
        lock (_gate)
        {
            snapshot = new List<IAprsBackend>(_backends.Count);
            foreach (var entry in _backends.Values)
            {
                snapshot.Add(entry.Backend);
            }
        }

        foreach (var backend in snapshot)
        {
            _ = SendSafeAsync(backend, packet);
        }
    }

    private static async Task SendSafeAsync(IAprsBackend backend, Models.AprsPacket packet)
    {
        try
        {
            await backend.SendAsync(packet);
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new AprsConnectionErrorMessage(ex.Message));
        }
    }

    private static async Task RunReceiveLoopAsync(IAprsBackend backend, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var packet in backend.ReceiveAsync(cancellationToken))
            {
                WeakReferenceMessenger.Default.Send(new AprsPacketReceivedMessage(packet));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new AprsConnectionErrorMessage(ex.Message));
        }
    }

    public async ValueTask DisposeAsync()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);

        List<Guid> handles;
        lock (_gate)
        {
            handles = new List<Guid>(_backends.Keys);
        }

        foreach (var handle in handles)
        {
            await UnregisterBackendAsync(handle);
        }
    }

    private sealed record BackendEntry(IAprsBackend Backend, CancellationTokenSource Cts, Task ReceiveTask);
}

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Aprs;

using AetherAprs.Messages;
using AetherAprs.Protocols;
using AetherAprs.Services.Logging;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class AprsSessionService : IAprsSessionService, IAsyncDisposable
{
    private readonly ILoggingService _log;
    private readonly Dictionary<Guid, BackendEntry> _backends = new();
    private readonly object _gate = new();

    public AprsSessionService(ILoggingService loggingService)
    {
        _log = loggingService.ForContext(nameof(AprsSessionService));
        _log.Debug("Constructed; subscribing to SendAprsPacketMessage");

        WeakReferenceMessenger.Default.Register<AprsSessionService, SendAprsPacketMessage>(this, static (service, message) =>
        {
            service.Broadcast(message.Value);
        });
    }

    public async Task<Guid> RegisterBackendAsync(IAprsBackend backend, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backend);

        var backendName = backend.GetType().Name;
        _log.Info($"Registering backend {backendName}");

        try
        {
            await backend.ConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Error($"Backend {backendName} failed to connect: {ex.Message}");
            throw;
        }

        var handle = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var task = RunReceiveLoopAsync(backend, backendName, cts.Token);

        lock (_gate)
        {
            _backends[handle] = new BackendEntry(backend, backendName, cts, task);
        }

        _log.Info($"Registered backend {backendName} as {handle:N} (total={_backends.Count})");
        return handle;
    }

    public async Task UnregisterBackendAsync(Guid handle, CancellationToken cancellationToken = default)
    {
        BackendEntry? entry;
        lock (_gate)
        {
            if (!_backends.Remove(handle, out entry))
            {
                _log.Warn($"Unregister called for unknown handle {handle:N}");
                return;
            }
        }

        _log.Info($"Unregistering backend {entry.Name} ({handle:N})");
        entry.Cts.Cancel();

        try
        {
            await entry.ReceiveTask.WaitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Warn($"Receive loop for {entry.Name} exited with error: {ex.Message}");
        }

        if (entry.Backend is IAsyncDisposable asyncDisposable)
        {
            try
            {
                await asyncDisposable.DisposeAsync();
            }
            catch (Exception ex)
            {
                _log.Error($"Backend {entry.Name} disposal threw: {ex.Message}");
            }
        }

        entry.Cts.Dispose();
        _log.Info($"Unregistered backend {entry.Name} (remaining={_backends.Count})");
    }

    private void Broadcast(Models.AprsPacket packet)
    {
        List<BackendEntry> snapshot;
        lock (_gate)
        {
            snapshot = new List<BackendEntry>(_backends.Values);
        }

        _log.Debug($"Broadcasting packet from {packet.Source} to {snapshot.Count} backend(s)");

        foreach (var entry in snapshot)
        {
            _ = SendSafeAsync(entry, packet);
        }
    }

    private async Task SendSafeAsync(BackendEntry entry, Models.AprsPacket packet)
    {
        try
        {
            await entry.Backend.SendAsync(packet);
            _log.Debug($"Sent packet from {packet.Source} via {entry.Name}");
        }
        catch (Exception ex)
        {
            _log.Error($"Send via {entry.Name} failed: {ex.Message}");
            WeakReferenceMessenger.Default.Send(new AprsConnectionErrorMessage(ex.Message));
        }
    }

    private async Task RunReceiveLoopAsync(IAprsBackend backend, string backendName, CancellationToken cancellationToken)
    {
        _log.Debug($"Receive loop started for {backendName}");
        try
        {
            await foreach (var packet in backend.ReceiveAsync(cancellationToken))
            {
                _log.Debug($"Received packet from {packet.Source} via {backendName} ({packet.Payload.Kind})");
                WeakReferenceMessenger.Default.Send(new AprsPacketReceivedMessage(packet));
            }
            _log.Info($"Receive loop for {backendName} ended (stream closed)");
        }
        catch (OperationCanceledException)
        {
            _log.Debug($"Receive loop for {backendName} cancelled");
        }
        catch (Exception ex)
        {
            _log.Error($"Receive loop for {backendName} failed: {ex.Message}");
            WeakReferenceMessenger.Default.Send(new AprsConnectionErrorMessage(ex.Message));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _log.Info("Disposing session; unregistering all backends");
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

    private sealed record BackendEntry(IAprsBackend Backend, string Name, CancellationTokenSource Cts, Task ReceiveTask);
}

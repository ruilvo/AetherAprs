// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Aprs;

using AetherAprs.Messages;
using AetherAprs.Models;
using AetherAprs.Protocols;
using AetherAprs.Services.Configuration;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class AprsSessionService(
    IAprsBackend aprsBackend,
    IConfigurationService configurationService) : IAprsSessionService, IDisposable
{
    private readonly IAprsBackend _aprsBackend = aprsBackend;
    private readonly IConfigurationService _configurationService = configurationService;
    private CancellationTokenSource? _receiveLoopCancellationTokenSource;
    private Task? _receiveLoopTask;
    private bool _isRegistered;

    public bool IsConnected { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        var settings = _configurationService.Settings;
        if (string.IsNullOrWhiteSpace(settings.Callsign) || string.IsNullOrWhiteSpace(settings.Passcode))
        {
            WeakReferenceMessenger.Default.Send(new AprsConnectionErrorMessage("Callsign and passcode are required before connecting to APRS-IS."));
            return;
        }

        await _aprsBackend.ConnectAsync(cancellationToken);

        if (!_isRegistered)
        {
            WeakReferenceMessenger.Default.Register<AprsSessionService, SendAprsPacketMessage>(this, static async (service, message) =>
            {
                if (!service.IsConnected)
                {
                    return;
                }

                try
                {
                    await service._aprsBackend.SendAsync(message.Value);
                }
                catch (Exception ex)
                {
                    WeakReferenceMessenger.Default.Send(new AprsConnectionErrorMessage(ex.Message));
                }
            });
            _isRegistered = true;
        }

        _receiveLoopCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveLoopTask = RunReceiveLoopAsync(_receiveLoopCancellationTokenSource.Token);
        IsConnected = true;
        WeakReferenceMessenger.Default.Send(new AprsConnectionStateChangedMessage(true));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        _receiveLoopCancellationTokenSource?.Cancel();
        if (_receiveLoopTask is not null)
        {
            await _receiveLoopTask.WaitAsync(cancellationToken);
        }

        if (_aprsBackend is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }

        IsConnected = false;
        WeakReferenceMessenger.Default.Send(new AprsConnectionStateChangedMessage(false));
    }

    private async Task RunReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var packet in _aprsBackend.ReceiveAsync(cancellationToken))
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

    public void Dispose()
    {
        if (_isRegistered)
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
            _isRegistered = false;
        }

        _receiveLoopCancellationTokenSource?.Cancel();
        _receiveLoopCancellationTokenSource?.Dispose();
    }
}

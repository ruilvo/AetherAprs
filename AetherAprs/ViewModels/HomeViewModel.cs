// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Messages;
using AetherAprs.Models;
using AetherAprs.Protocols;
using AetherAprs.Services.Aprs;
using AetherAprs.Services.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels;

/// <summary>
/// View model for the home page, displaying the APRS map view.
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    private readonly IAprsSessionService _aprsSessionService;
    private readonly IAprsBackend _aprsIsBackend;
    private readonly ILoggingService _log;
    private Guid? _aprsIsHandle;

    [ObservableProperty]
    public partial IEnumerable<Point> MarkerLocations { get; set; }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ConnectionStatus { get; set; }

    public HomeViewModel(IAprsSessionService aprsSessionService, IAprsBackend aprsIsBackend, ILoggingService loggingService)
    {
        _aprsSessionService = aprsSessionService;
        _aprsIsBackend = aprsIsBackend;
        _log = loggingService.ForContext(nameof(HomeViewModel));
        _log.Debug("Constructed.");

        MarkerLocations =
        [
            new Point(new Coordinate{Y= 38.7223, X=-9.1393 }),
        ];

        WeakReferenceMessenger.Default.Register<HomeViewModel, MapMarkersRequestMessage>(this, (r, m) =>
        {
            m.Reply(r.MarkerLocations);
        });

        WeakReferenceMessenger.Default.Register<HomeViewModel, AprsPacketReceivedMessage>(this, static (recipient, message) =>
        {
            recipient.HandleReceivedPacket(message.Value);
        });

        WeakReferenceMessenger.Default.Register<HomeViewModel, AprsConnectionErrorMessage>(this, static (recipient, message) =>
        {
            recipient._log.Warn($"Connection error surfaced to UI: {message.Value}.");
            recipient.IsBusy = false;
            recipient.ConnectionStatus = message.Value;
        });
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_aprsIsHandle is not null)
        {
            _log.Debug("Connect command ignored; already connected.");
            return;
        }

        _log.Info("User requested APRS-IS connect.");
        IsBusy = true;
        ConnectionStatus = "Connecting to APRS-IS...";
        try
        {
            _aprsIsHandle = await _aprsSessionService.RegisterBackendAsync(_aprsIsBackend);
            IsConnected = true;
            ConnectionStatus = "Connected to APRS-IS.";
            _log.Info("APRS-IS connect succeeded.");
        }
        catch (Exception ex)
        {
            _log.Error($"APRS-IS connect failed: {ex.Message}.");
            ConnectionStatus = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_aprsIsHandle is not { } handle)
        {
            _log.Debug("Disconnect command ignored; not connected.");
            return;
        }

        _log.Info("User requested APRS-IS disconnect.");
        IsBusy = true;
        ConnectionStatus = "Disconnecting from APRS-IS...";
        try
        {
            await _aprsSessionService.UnregisterBackendAsync(handle);
            _aprsIsHandle = null;
            IsConnected = false;
            ConnectionStatus = "Disconnected.";
            _log.Info("APRS-IS disconnect complete.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnMarkerLocationsChanged(IEnumerable<Point> value)
    {
        WeakReferenceMessenger.Default.Send(new MapMarkersChangedMessage(value));
    }

    private void HandleReceivedPacket(AprsPacket packet)
    {
        if (packet.Payload.Location is null)
        {
            return;
        }

        MarkerLocations =
        [
            ..MarkerLocations,
            packet.Payload.Location,
        ];
    }
}

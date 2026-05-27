// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Messages;
using AetherAprs.Models;
using AetherAprs.Protocols;
using AetherAprs.Services.Aprs;
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
    private Guid? _aprsIsHandle;

    [ObservableProperty]
    public partial IEnumerable<Point> MarkerLocations { get; set; }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ConnectionStatus { get; set; }

    public HomeViewModel(IAprsSessionService aprsSessionService, IAprsBackend aprsIsBackend)
    {
        _aprsSessionService = aprsSessionService;
        _aprsIsBackend = aprsIsBackend;

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
            recipient.IsBusy = false;
            recipient.ConnectionStatus = message.Value;
        });
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_aprsIsHandle is not null)
        {
            return;
        }

        IsBusy = true;
        ConnectionStatus = "Connecting to APRS-IS...";
        try
        {
            _aprsIsHandle = await _aprsSessionService.RegisterBackendAsync(_aprsIsBackend);
            IsConnected = true;
            ConnectionStatus = "Connected to APRS-IS.";
        }
        catch (Exception ex)
        {
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
            return;
        }

        IsBusy = true;
        ConnectionStatus = "Disconnecting from APRS-IS...";
        try
        {
            await _aprsSessionService.UnregisterBackendAsync(handle);
            _aprsIsHandle = null;
            IsConnected = false;
            ConnectionStatus = "Disconnected.";
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

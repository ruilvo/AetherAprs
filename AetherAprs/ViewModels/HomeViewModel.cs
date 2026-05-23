// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Messages;
using AetherAprs.Models;
using AetherAprs.Services.Aprs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NetTopologySuite.Geometries;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels;

/// <summary>
/// View model for the home page, displaying the APRS map view.
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    private readonly IAprsSessionService _aprsSessionService;

    [ObservableProperty]
    public partial IEnumerable<Point> MarkerLocations { get; set; }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ConnectionStatus { get; set; }

    public HomeViewModel(IAprsSessionService aprsSessionService)
    {
        _aprsSessionService = aprsSessionService;
        IsConnected = aprsSessionService.IsConnected;

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

        WeakReferenceMessenger.Default.Register<HomeViewModel, AprsConnectionStateChangedMessage>(this, static (recipient, message) =>
        {
            recipient.IsConnected = message.Value;
            recipient.IsBusy = false;
            recipient.ConnectionStatus = message.Value ? "Connected to APRS-IS." : "Disconnected.";
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
        IsBusy = true;
        ConnectionStatus = "Connecting to APRS-IS...";
        await _aprsSessionService.StartAsync();
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        IsBusy = true;
        ConnectionStatus = "Disconnecting from APRS-IS...";
        await _aprsSessionService.StopAsync();
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

// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Localization;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Location;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geo.Abstractions.Interfaces;
using Geo.Geodesy;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// ViewModel for packet details page showing all packets from a specific callsign,
/// with tabs for history, position trail, raw packets, and weather/status/telemetry,
/// plus distance &amp; bearing from the user's current location.
/// </summary>
public partial class PacketDetailsViewModel(
    IPacketQueryService packetQueryService,
    INavigationService navigationService,
    IConversationViewModelFactory conversationFactory,
    ILocationTrackingService locationTrackingService,
    ICompassService compassService,
    ILogger<PacketDetailsViewModel> logger) : ViewModelBase, IOverlayRetireable
{
    private readonly IGeodeticCalculator _geodeticCalculator = new SpheroidCalculator(Spheroid.Wgs84);
    private double? _bearingDegrees;

    [ObservableProperty]
    public partial string Callsign { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Title { get; set; } = Strings.Get("PacketDetailsTitle");

    [ObservableProperty]
    public partial ObservableCollection<PacketRecord> Packets { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<PacketRecord> PositionHistory { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<PacketRecord> WeatherStatusTelemetry { get; set; } = [];

    public bool HasPackets => Packets.Count > 0;

    public bool HasPositionHistory => PositionHistory.Count > 0;

    public bool HasWeatherStatusTelemetry => WeatherStatusTelemetry.Count > 0;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    public partial bool HasDistance { get; set; }

    [ObservableProperty]
    public partial string DistanceText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BearingText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double RelativeBearingDegrees { get; set; }

    public void Initialize(string callsign)
    {
        logger.LogInformation("Initializing PacketDetailsViewModel with callsign: {Callsign}", callsign);
        Callsign = callsign;
        Title = Strings.Format("PacketsFromCallsign", callsign);

        locationTrackingService.LocationUpdated += OnLocationUpdated;
        compassService.HeadingUpdated += OnHeadingUpdated;
        compassService.StartListening();

        _ = LoadPacketsAsync();
    }

    /// <inheritdoc />
    public void OnRetiredFromOverlay()
    {
        locationTrackingService.LocationUpdated -= OnLocationUpdated;
        compassService.HeadingUpdated -= OnHeadingUpdated;
        compassService.StopListening();
    }

    private void OnLocationUpdated(object? sender, LocationData location) => UpdateDistanceAndBearing();

    private void OnHeadingUpdated(object? sender, double heading) => UpdateDistanceAndBearing();

    private async Task LoadPacketsAsync()
    {
        try
        {
            IsLoading = true;

            logger.LogInformation("Loading packets for callsign: {Callsign}", Callsign);

            IReadOnlyList<PacketRecord> packets = await packetQueryService.GetPacketsByCallsignAsync(Callsign, 500);

            logger.LogInformation("Found {Count} packets for callsign {Callsign}", packets.Count, Callsign);

            Packets.Clear();
            PositionHistory.Clear();
            WeatherStatusTelemetry.Clear();
            foreach (PacketRecord packet in packets)
            {
                Packets.Add(packet);

                if (packet.Position != null)
                {
                    PositionHistory.Add(packet);
                }

                if (packet.Weather != null || packet.Status != null
                    || packet.Telemetry != null || packet.Capabilities != null)
                {
                    WeatherStatusTelemetry.Add(packet);
                }
            }

            UpdateDistanceAndBearing();

            logger.LogInformation("Added {Count} packets to observable collection", Packets.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load packets for {Callsign}", Callsign);
        }
        finally
        {
            OnPropertyChanged(nameof(HasPackets));
            OnPropertyChanged(nameof(HasPositionHistory));
            OnPropertyChanged(nameof(HasWeatherStatusTelemetry));
            IsLoading = false;
        }
    }

    private void UpdateDistanceAndBearing()
    {
        LocationData? userLocation = locationTrackingService.CurrentLocation;
        PositionDataRecord? latestPosition = Packets.FirstOrDefault(p => p.Position != null)?.Position;

        if (userLocation is null || latestPosition is null)
        {
            HasDistance = false;
            DistanceText = string.Empty;
            BearingText = string.Empty;
            RelativeBearingDegrees = 0;
            return;
        }

        GeodeticLine? line = _geodeticCalculator.CalculateOrthodromicLine(
            userLocation.Location,
            latestPosition.Location);

        if (line is null)
        {
            // Identical points: zero distance and undefined bearing.
            _bearingDegrees = 0;
            HasDistance = true;
            DistanceText = Strings.Format("DistanceMetersFormat", 0);
            BearingText = Strings.Format("BearingDegreesFormat", 0);
            UpdateRelativeBearing();
            return;
        }

        double distanceMeters = line.Distance.SiValue;
        double bearingDegrees = line.Bearing12;

        _bearingDegrees = bearingDegrees;
        HasDistance = true;
        DistanceText = distanceMeters < 1000
            ? Strings.Format("DistanceMetersFormat", distanceMeters)
            : Strings.Format("DistanceKilometersFormat", distanceMeters / 1000.0);

        BearingText = Strings.Format("BearingDegreesFormat", bearingDegrees);
        UpdateRelativeBearing();
    }

    private void UpdateRelativeBearing()
    {
        if (_bearingDegrees is not double bearing)
        {
            RelativeBearingDegrees = 0;
            return;
        }

        double? heading = compassService.HeadingDegrees;
        RelativeBearingDegrees = NormalizeDegrees(heading.HasValue ? bearing - heading.Value : bearing);
    }

    private static double NormalizeDegrees(double value)
    {
        double result = value % 360.0;
        return result < 0 ? result + 360.0 : result;
    }

    [RelayCommand]
    private void SendMessage()
    {
        // Navigate to messages page and open conversation with this callsign
        navigationService.NavigateTo<MessagesViewModel>();

        // The message is addressed to the station, so the SSID is part of the
        // destination; dropping it would deliver to N0CALL instead of N0CALL-7.
        if (Models.Aprs.Callsign.TryParse(Callsign, out Callsign callsign))
        {
            AprsAddress address = AprsAddress.FromCallsign(callsign);
            ConversationViewModel conversationVm = conversationFactory.Create(address);
            navigationService.NavigateTo(conversationVm);
        }
        else
        {
            logger.LogError("Failed to parse callsign {Callsign}", Callsign);
        }
    }
}

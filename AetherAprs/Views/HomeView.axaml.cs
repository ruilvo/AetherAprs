// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Messages;
using AetherAprs.Models.Geo;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.Views;

public partial class HomeView : UserControl
{
    // For MapsUi
    private readonly MemoryLayer _markerLayer;

    private void UpdateMapMarkers(List<GeoLocation> locations)
    {
        var mapFeatures = locations
            .Select(x => (IFeature)new PointFeature(SphericalMercator.FromLonLat(x.Longitude, x.Latitude)))
            .ToList();

        _markerLayer.Features = mapFeatures;
    }

    public HomeView()
    {
        InitializeComponent();

        MapControl.Map.Layers.Add(OpenStreetMap.CreateTileLayer());

        _markerLayer = new MemoryLayer
        {
            Name = "MarkerLayer",
            Style = new SymbolStyle
            {
                SymbolScale = 0.5,
                Fill = new Brush(Color.Red)
            },
            Features = []
        };
        MapControl.Map.Layers.Add(_markerLayer);

        WeakReferenceMessenger.Default.Register<HomeView, MapMarkersChangedMessage>(this, (recipient, message) =>
        {
            recipient.UpdateMapMarkers(message.Value);
        });

        var initialState = WeakReferenceMessenger.Default.Send<MapMarkersRequestMessage>();
        if (initialState.HasReceivedResponse)
        {
            UpdateMapMarkers(initialState.Response);
        }
    }
}

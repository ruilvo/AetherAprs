// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Messages;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;

namespace AetherAprs.Views;

public partial class HomeView : UserControl
{
    // For MapsUi
    private readonly MemoryLayer _markerLayer;

    private void UpdateMapMarkers(IEnumerable<Point> locations)
    {
        _markerLayer.Features = locations
            .Select(p =>
            {
                var feature = new PointFeature(SphericalMercator.FromLonLat(p.X, p.Y));
                feature.Styles.Add(new SymbolStyle
                {
                    SymbolScale = 0.5,
                    Fill = new Brush(Color.Red),
                });
                return (IFeature)feature;
            });
    }

    public HomeView()
    {
        InitializeComponent();

        MapControl.Map.Layers.Add(OpenStreetMap.CreateTileLayer());

        _markerLayer = new MemoryLayer
        {
            Name = "MarkerLayer",
            Features = [],
            Style = null, // Styles are defined per feature
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

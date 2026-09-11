// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;

namespace AetherAprs.ViewModels;

/// <summary>
/// Manages the display of received APRS beacons on a Mapsui map layer.
/// Subscribes to PortService PacketReceived events and updates features
/// with correct APRS symbols and positions.
/// </summary>
public sealed class ReceivedBeaconsViewModel : IDisposable
{
    private readonly IPortService _portService;
    private readonly AprsSymbolMapConverter _symbolConverter;
    private readonly WritableLayer _beaconsLayer;
    private readonly Dictionary<string, PointFeature> _beaconsByCallsign = new();
    private bool _disposed;

    /// <summary>
    /// Gets the layer containing all received beacon features.
    /// </summary>
    public WritableLayer BeaconsLayer => _beaconsLayer;

    public ReceivedBeaconsViewModel(
        IPortService portService,
        IAprsSymbolBitmapProvider symbolBitmapProvider)
    {
        _portService = portService ?? throw new ArgumentNullException(nameof(portService));
        _symbolConverter = new AprsSymbolMapConverter(
            symbolBitmapProvider ?? throw new ArgumentNullException(nameof(symbolBitmapProvider)));

        _beaconsLayer = new WritableLayer
        {
            Name = "Received Beacons"
        };

        // Subscribe to received packets
        _portService.PacketReceived += OnPortServicePacketReceived;
    }

    private void OnPortServicePacketReceived(object? sender, AprsPacket packet)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Only display position packets on the map
        if (packet is not PositionPacket positionPacket)
        {
            return;
        }

        // Marshal to UI thread since this event may be raised from a background thread
        Dispatcher.UIThread.Post(() => UpdateBeaconOnMap(positionPacket));
    }

    private void UpdateBeaconOnMap(PositionPacket positionPacket)
    {
        // Create a unique key for this beacon (callsign + SSID)
        var beaconKey = positionPacket.Source.ToString();

        // Convert geographic coordinates to Spherical Mercator projection
        var mercatorCoordinate = SphericalMercator.FromLonLat(
            positionPacket.Longitude,
            positionPacket.Latitude);
        var mapPoint = new MPoint(mercatorCoordinate.x, mercatorCoordinate.y);

        // Create the ImageStyle with the correct APRS symbol (smaller scale)
        var imageStyle = _symbolConverter.CreateImageStyle(positionPacket.Symbol, scale: 0.15);

        // Create a text label style for the callsign (no background)
        var labelStyle = new LabelStyle
        {
            Text = beaconKey,
            Offset = new Offset(35, 0), // Offset to the right of the icon
            Font = new Mapsui.Styles.Font { FontFamily = "Arial", Size = 10 },
            ForeColor = Color.Black,
            BackColor = null, // Transparent background
            Halo = null // No halo effect
        };

        // Create a transparent symbol style to override any default backgrounds
        var transparentSymbolStyle = new SymbolStyle
        {
            Fill = null,
            Outline = null
        };

        // Create new feature for this beacon at the position
        var feature = new PointFeature(mapPoint)
        {
            Styles = [transparentSymbolStyle, imageStyle, labelStyle]
        };

        // Update or add beacon
        if (_beaconsByCallsign.TryGetValue(beaconKey, out var existingFeature))
        {
            // Remove old feature
            _beaconsLayer.TryRemove(existingFeature);
        }

        _beaconsByCallsign[beaconKey] = feature;
        _beaconsLayer.Add(feature);

        // Notify the map that the layer has changed
        _beaconsLayer.DataHasChanged();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _portService.PacketReceived -= OnPortServicePacketReceived;
        _beaconsLayer.Clear();
        _beaconsByCallsign.Clear();
        _symbolConverter.Dispose();
    }
}


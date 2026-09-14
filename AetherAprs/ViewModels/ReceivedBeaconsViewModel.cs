// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
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
/// Subscribes to PortService packet events and updates features with APRS symbols.
/// Port ShowOnMap is a visual filter only; history is retained while a port is hidden.
/// </summary>
public sealed class ReceivedBeaconsViewModel : IDisposable
{
    private readonly IPortService _portService;
    private readonly AprsSymbolMapConverter _symbolConverter;
    private readonly WritableLayer _beaconsLayer;
    private readonly Dictionary<(Guid PortId, string Callsign), BeaconHistoryEntry> _history = new();
    private readonly Dictionary<string, (Symbol symbol, ImageStyle imageStyle)> _symbolStyleCache = new();
    private bool _disposed;

    /// <summary>
    /// Gets the layer containing all received beacon features.
    /// Returns ILayer to prevent external modification of the internal WritableLayer.
    /// </summary>
    public ILayer BeaconsLayer => _beaconsLayer;

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

        _portService.PacketReceived += OnPortServicePacketReceived;
        _portService.PortsChanged += OnPortsChanged;
    }

    private void OnPortServicePacketReceived(object? sender, PortPacketReceivedEventArgs e)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (e.Packet is not PositionPacket positionPacket)
        {
            return;
        }

        RunOnUi(() => UpsertHistoryAndRefresh(e.PortId, positionPacket));
    }

    private void OnPortsChanged(object? sender, EventArgs e)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RunOnUi(RebuildVisibleLayer);
    }

    private static void RunOnUi(Action action)
    {
        // Unit tests have no Avalonia application; apply immediately.
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private void UpsertHistoryAndRefresh(Guid portId, PositionPacket positionPacket)
    {
        var callsign = positionPacket.Source.ToString();
        _history[(portId, callsign)] = new BeaconHistoryEntry
        {
            Packet = positionPacket,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        RebuildVisibleLayer();
    }

    private void RebuildVisibleLayer()
    {
        var visiblePortIds = _portService.Ports
            .Where(p => p.ShowOnMap)
            .Select(p => p.Id)
            .ToHashSet();

        // Drop history for ports that no longer exist.
        var knownPortIds = _portService.Ports.Select(p => p.Id).ToHashSet();
        foreach (var key in _history.Keys.Where(k => !knownPortIds.Contains(k.PortId)).ToArray())
        {
            _history.Remove(key);
        }

        var latestByCallsign = _history
            .Where(pair => visiblePortIds.Contains(pair.Key.PortId))
            .GroupBy(pair => pair.Key.Callsign)
            .Select(group => group.OrderByDescending(pair => pair.Value.ReceivedAt).First())
            .ToList();

        _beaconsLayer.Clear();

        foreach (var pair in latestByCallsign)
        {
            var feature = CreateFeature(pair.Value.Packet, pair.Key.Callsign);
            _beaconsLayer.Add(feature);
        }

        _beaconsLayer.DataHasChanged();
    }

    private PointFeature CreateFeature(PositionPacket positionPacket, string beaconKey)
    {
        var mercatorCoordinate = SphericalMercator.FromLonLat(
            positionPacket.Longitude,
            positionPacket.Latitude);
        var mapPoint = new MPoint(mercatorCoordinate.x, mercatorCoordinate.y);

        var symbolKey = $"{positionPacket.Symbol.TableChar}{positionPacket.Symbol.CodeChar}";
        if (!_symbolStyleCache.TryGetValue(symbolKey, out var cachedStyle) ||
            cachedStyle.symbol != positionPacket.Symbol)
        {
            var imageStyle = _symbolConverter.CreateImageStyle(positionPacket.Symbol, scale: 0.15);
            cachedStyle = (positionPacket.Symbol, imageStyle);
            _symbolStyleCache[symbolKey] = cachedStyle;
        }

        var labelStyle = new LabelStyle
        {
            Text = beaconKey,
            Offset = new Offset(35, 0),
            Font = new Mapsui.Styles.Font { FontFamily = "Arial", Size = 10 },
            ForeColor = Color.Black,
            BackColor = null,
            Halo = null
        };

        return new PointFeature(mapPoint)
        {
            Styles = [cachedStyle.imageStyle, labelStyle]
        };
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
        _portService.PortsChanged -= OnPortsChanged;
        _beaconsLayer.Clear();
        _history.Clear();
        _symbolStyleCache.Clear();
        _symbolConverter.Dispose();
    }

    private sealed class BeaconHistoryEntry
    {
        public required PositionPacket Packet { get; set; }

        public required DateTimeOffset ReceivedAt { get; set; }
    }
}

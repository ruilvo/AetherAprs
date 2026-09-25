// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using Avalonia;
using Avalonia.Threading;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.ViewModels;

/// <summary>
/// Manages the display of received APRS beacons on a Mapsui map layer.
/// Uses the shared PacketCacheService and updates features with APRS symbols.
/// Port ShowOnMap is a visual filter only.
/// </summary>
public sealed class ReceivedBeaconsViewModel : IDisposable
{
    private readonly IPortService _portService;
    private readonly IPacketCacheService _packetCacheService;
    private readonly AprsSymbolMapConverter _symbolConverter;
    private readonly ILogger<ReceivedBeaconsViewModel> _logger;
    private readonly WritableLayer _beaconsLayer;
    private readonly Dictionary<string, (Symbol symbol, ImageStyle imageStyle)> _symbolStyleCache = new();
    private bool _disposed;

    /// <summary>
    /// Gets the layer containing all received beacon features.
    /// Returns ILayer to prevent external modification of the internal WritableLayer.
    /// </summary>
    public ILayer BeaconsLayer => _beaconsLayer;

    public ReceivedBeaconsViewModel(
        IPortService portService,
        IPacketCacheService packetCacheService,
        IAprsSymbolBitmapProvider symbolBitmapProvider,
        ILogger<ReceivedBeaconsViewModel> logger)
    {
        _portService = portService ?? throw new ArgumentNullException(nameof(portService));
        _packetCacheService = packetCacheService ?? throw new ArgumentNullException(nameof(packetCacheService));
        _symbolConverter = new AprsSymbolMapConverter(
            symbolBitmapProvider ?? throw new ArgumentNullException(nameof(symbolBitmapProvider)));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _beaconsLayer = new WritableLayer
        {
            Name = "Received Beacons",
            // Mapsui layers default to a white VectorStyle with a grey outline.
            Style = null
        };

        _packetCacheService.CacheUpdated += OnCacheUpdated;
        _portService.PortsChanged += OnPortsChanged;
    }

    private void OnCacheUpdated(object? sender, PacketCacheUpdatedEventArgs e)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (e.UpdatedPacket.Packet is not PositionPacket)
        {
            _logger.LogDebug(
                "Ignoring non-position packet Type={PacketType}",
                e.UpdatedPacket.Packet.GetType().Name);
            return;
        }

        RunOnUi(RebuildVisibleLayer);
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

    private void RebuildVisibleLayer()
    {
        var visiblePortIds = _portService.Ports
            .Where(p => p.ShowOnMap)
            .Select(p => p.Id)
            .ToHashSet();

        // Get position packets from cache
        var positionPackets = _packetCacheService.GetPositionPackets();

        // Filter by visible ports
        var visiblePackets = positionPackets.Values
            .Where(cp => visiblePortIds.Contains(cp.PortId))
            .ToList();

        _beaconsLayer.Clear();

        foreach (var cachedPacket in visiblePackets)
        {
            if (cachedPacket.Packet is PositionPacket positionPacket)
            {
                _logger.LogDebug(
                    "Adding map beacon for {Callsign} at Lat={Latitude:F5}, Lon={Longitude:F5}",
                    cachedPacket.Source,
                    positionPacket.Latitude,
                    positionPacket.Longitude);

                var feature = CreateFeature(positionPacket, cachedPacket.Source);
                _beaconsLayer.Add(feature);
            }
        }

        _beaconsLayer.DataHasChanged();
    }

    private PointFeature CreateFeature(PositionPacket positionPacket, string beaconKey)
    {
        var (x, y) = SphericalMercator.FromLonLat(
            positionPacket.Longitude,
            positionPacket.Latitude);
        var mapPoint = new MPoint(x, y);

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
            Font = new Font { FontFamily = "Arial", Size = 10 },
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
        _packetCacheService.CacheUpdated -= OnCacheUpdated;
        _portService.PortsChanged -= OnPortsChanged;
        _beaconsLayer.Clear();
        _symbolStyleCache.Clear();
        _symbolConverter.Dispose();
    }
}

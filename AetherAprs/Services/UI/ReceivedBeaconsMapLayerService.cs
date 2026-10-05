// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Data.Entities;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Ports;
using Avalonia;
using Avalonia.Threading;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.Services.UI;

/// <summary>
/// Service that provides map layers for displaying received APRS beacons and position trails.
/// Queries packet data from the database and updates map features with APRS symbols.
/// </summary>
public interface IReceivedBeaconsMapLayerService : IDisposable
{
    /// <summary>
    /// Gets the layer containing all received beacon markers.
    /// </summary>
    ILayer BeaconsLayer { get; }

    /// <summary>
    /// Gets the layer containing position trails.
    /// </summary>
    ILayer TrailsLayer { get; }
}

/// <summary>
/// Implementation of received beacons map layer service.
/// </summary>
public sealed class ReceivedBeaconsMapLayerService : IReceivedBeaconsMapLayerService
{
    private readonly IPortService _portService;
    private readonly IPacketQueryService _packetQueryService;
    private readonly IPacketStorageService _packetStorageService;
    private readonly IConfigurationService _configurationService;
    private readonly AprsSymbolMapConverter _symbolConverter;
    private readonly ILogger<ReceivedBeaconsMapLayerService> _logger;
    private readonly WritableLayer _beaconsLayer;
    private readonly WritableLayer _trailsLayer;
    private readonly Dictionary<string, (Symbol symbol, ImageStyle imageStyle)> _symbolStyleCache = new();
    private bool _disposed;

    public ILayer BeaconsLayer => _beaconsLayer;
    public ILayer TrailsLayer => _trailsLayer;

    public ReceivedBeaconsMapLayerService(
        IPortService portService,
        IPacketQueryService packetQueryService,
        IPacketStorageService packetStorageService,
        IConfigurationService configurationService,
        IAprsSymbolBitmapProvider symbolBitmapProvider,
        ILogger<ReceivedBeaconsMapLayerService> logger)
    {
        _portService = portService ?? throw new ArgumentNullException(nameof(portService));
        _packetQueryService = packetQueryService ?? throw new ArgumentNullException(nameof(packetQueryService));
        _packetStorageService = packetStorageService ?? throw new ArgumentNullException(nameof(packetStorageService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _symbolConverter = new AprsSymbolMapConverter(
            symbolBitmapProvider ?? throw new ArgumentNullException(nameof(symbolBitmapProvider)));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _beaconsLayer = new WritableLayer
        {
            Name = "Received Beacons",
            Style = null
        };

        _trailsLayer = new WritableLayer
        {
            Name = "Position Trails",
            Style = null
        };

        _packetStorageService.PacketStored += OnPacketStored;
        _portService.PortsChanged += OnPortsChanged;
    }

    private void OnPacketStored(object? sender, EventArgs e)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RunOnUi(() => _ = RebuildVisibleLayersAsync());
    }

    private void OnPortsChanged(object? sender, EventArgs e)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RunOnUi(() => _ = RebuildVisibleLayersAsync());
    }

    private static void RunOnUi(Action action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private async Task RebuildVisibleLayersAsync()
    {
        try
        {
            var visiblePortIds = _portService.Ports
                .Where(p => p.ShowOnMap)
                .Select(p => p.Id)
                .ToHashSet();

            var timeRange = _configurationService.Settings.Aprs.DisplayTimeRange;
            var customHours = _configurationService.Settings.Aprs.CustomDisplayTimeRangeHours;
            var cutoffTime = GetCutoffTime(timeRange, customHours);

            var allPositionPackets = await _packetQueryService.GetMostRecentPositionPacketsAsync();

            var filteredPackets = allPositionPackets.Values
                .Where(r => !cutoffTime.HasValue || new DateTimeOffset(r.Timestamp, TimeSpan.Zero) >= cutoffTime.Value);

            filteredPackets = filteredPackets.Where(r => r.PortId.HasValue && visiblePortIds.Contains(r.PortId.Value));

            var packetRecords = filteredPackets.ToList();

            _beaconsLayer.Clear();
            _trailsLayer.Clear();

            foreach (var record in packetRecords)
            {
                if (record.Position != null)
                {
                    var callsign = FormatCallsign(record.SourceBase, record.SourceSsid);
                    _logger.LogDebug(
                        "Adding map beacon for {Callsign} at Lat={Latitude:F5}, Lon={Longitude:F5}",
                        callsign,
                        record.Position.Location.Latitude,
                        record.Position.Location.Longitude);

                    var feature = CreateFeatureFromRecord(record);
                    _beaconsLayer.Add(feature);
                }
            }

            foreach (var record in packetRecords)
            {
                var callsign = FormatCallsign(record.SourceBase, record.SourceSsid);
                var trailRecords = await _packetQueryService.GetPacketsByCallsignAsync(callsign, limit: 100);

                var trailPositions = trailRecords
                    .Where(r => r.Position != null)
                    .Where(r => !cutoffTime.HasValue || new DateTimeOffset(r.Timestamp, TimeSpan.Zero) >= cutoffTime.Value)
                    .OrderBy(r => r.Timestamp)
                    .ToList();

                if (trailPositions.Count > 1)
                {
                    var trailFeature = CreateTrailFeature(trailPositions);
                    if (trailFeature != null)
                    {
                        _trailsLayer.Add(trailFeature);
                    }
                }
            }

            _beaconsLayer.DataHasChanged();
            _trailsLayer.DataHasChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild map layers");
        }
    }

    private static DateTimeOffset? GetCutoffTime(PacketDisplayTimeRange timeRange, int customHours)
    {
        var now = DateTimeOffset.UtcNow;
        return timeRange switch
        {
            PacketDisplayTimeRange.LastHour => now.AddHours(-1),
            PacketDisplayTimeRange.LastDay => now.AddDays(-1),
            PacketDisplayTimeRange.LastWeek => now.AddDays(-7),
            PacketDisplayTimeRange.LastMonth => now.AddDays(-30),
            PacketDisplayTimeRange.Custom => now.AddHours(-customHours),
            PacketDisplayTimeRange.All => null,
            _ => now.AddDays(-1)
        };
    }

    private GeometryFeature? CreateTrailFeature(List<PacketRecord> trail)
    {
        if (trail.Count < 2)
        {
            return null;
        }

        var coordinates = trail
            .Where(p => p.Position != null)
            .Select(p =>
            {
                var (x, y) = SphericalMercator.FromLonLat(p.Position!.Location.Longitude, p.Position!.Location.Latitude);
                return new Coordinate(x, y);
            })
            .ToArray();

        if (coordinates.Length < 2)
        {
            return null;
        }

        var lineString = new LineString(coordinates);
        var feature = new GeometryFeature { Geometry = lineString };

        feature.Styles.Add(new VectorStyle
        {
            Line = new Pen(Color.FromArgb(200, 0, 120, 215), 2)
        });

        return feature;
    }

    private PointFeature CreateFeatureFromRecord(PacketRecord record)
    {
        var (x, y) = SphericalMercator.FromLonLat(
            record.Position!.Location.Longitude,
            record.Position!.Location.Latitude);
        var mapPoint = new MPoint(x, y);

        var symbol = new Symbol(record.Position.SymbolTable, record.Position.SymbolCode);

        var symbolKey = $"{record.Position.SymbolTable.ToChar()}{record.Position.SymbolCode.ToChar()}";
        if (!_symbolStyleCache.TryGetValue(symbolKey, out var cachedStyle) ||
            cachedStyle.symbol != symbol)
        {
            var imageStyle = _symbolConverter.CreateImageStyle(symbol, scale: 0.25);
            cachedStyle = (symbol, imageStyle);
            _symbolStyleCache[symbolKey] = cachedStyle;
        }

        var callsign = FormatCallsign(record.SourceBase, record.SourceSsid);
        var labelStyle = new LabelStyle
        {
            Text = callsign,
            Offset = new Offset(45, 0),
            Font = new Font { FontFamily = "Arial", Size = 14, Bold = true },
            ForeColor = Color.Black,
            BackColor = new Brush(new Color(255, 255, 255, 200)),
            Halo = new Pen(Color.White, 2)
        };

        return new PointFeature(mapPoint)
        {
            Styles = [cachedStyle.imageStyle, labelStyle],
            ["Callsign"] = callsign
        };
    }

    private static string FormatCallsign(string baseCallsign, AprsSsid? ssid)
    {
        return ssid.HasValue && ssid.Value != AprsSsid.PrimaryStation
            ? $"{baseCallsign}-{(byte)ssid.Value}"
            : baseCallsign;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _packetStorageService.PacketStored -= OnPacketStored;
        _portService.PortsChanged -= OnPortsChanged;
        _beaconsLayer.Clear();
        _trailsLayer.Clear();
        _symbolStyleCache.Clear();
        _symbolConverter.Dispose();
    }
}

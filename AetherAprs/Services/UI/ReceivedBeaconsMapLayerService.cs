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
using System.Threading;
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

    /// <summary>
    /// Updates the current viewport resolution (world units per pixel) and re-clusters
    /// the beacon markers accordingly.
    /// </summary>
    void SetResolution(double resolution);
}

/// <summary>
/// Implementation of received beacons map layer service.
/// </summary>
public sealed class ReceivedBeaconsMapLayerService : IReceivedBeaconsMapLayerService
{
    private const int MaxTrailPointsPerStation = 20;
    private const double MaxTrailJumpMeters = 100_000;
    private const double MaxTrailResolutionMetersPerPixel = 1_000;

    private readonly IPortService _portService;
    private readonly IPacketQueryService _packetQueryService;
    private readonly IPacketStorageService _packetStorageService;
    private readonly IConfigurationService _configurationService;
    private readonly AprsSymbolMapConverter _symbolConverter;
    private readonly ILogger<ReceivedBeaconsMapLayerService> _logger;
    private readonly DateTimeOffset _sessionStartedUtc = DateTimeOffset.UtcNow;
    private readonly WritableLayer _beaconsLayer;
    private readonly WritableLayer _trailsLayer;
    private readonly Dictionary<string, (Symbol symbol, ImageStyle imageStyle)> _symbolStyleCache = new();
    private readonly Dictionary<string, GeometryFeature> _trailFeatures = new();
    private readonly HashSet<string> _clusteredCallsigns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _rebuildLock = new();
    private bool _rebuildPending;
    private bool _rebuildRunning;
    private bool _disposed;
    private List<BeaconMarker> _markers = [];
    private double _currentResolution = double.NaN;

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
        if (_disposed)
        {
            return;
        }

        ScheduleRebuild();
    }

    private void OnPortsChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        ScheduleRebuild();
    }

    /// <summary>
    /// Coalesces rebuild requests: at most one rebuild runs at a time, and events
    /// arriving during a rebuild schedule exactly one more (H2). This prevents
    /// per-packet full rebuilds under an active APRS-IS feed and overlapping
    /// rebuilds racing on the shared layers.
    /// </summary>
    private void ScheduleRebuild()
    {
        var startRequested = false;

        lock (_rebuildLock)
        {
            if (_disposed)
            {
                return;
            }

            if (_rebuildRunning)
            {
                _rebuildPending = true;
                return;
            }

            _rebuildRunning = true;
            startRequested = true;
        }

        if (startRequested)
        {
            RunOnUi(() => _ = RebuildLoopAsync());
        }
    }

    private async Task RebuildLoopAsync()
    {
        try
        {
            await RebuildVisibleLayersAsync();
        }
        finally
        {
            var rerun = false;
            lock (_rebuildLock)
            {
                if (_rebuildPending && !_disposed)
                {
                    _rebuildPending = false;
                    rerun = true;
                }
                else
                {
                    _rebuildRunning = false;
                }
            }

            if (rerun)
            {
                ScheduleRebuild();
            }
        }
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
            if (!cutoffTime.HasValue || _sessionStartedUtc > cutoffTime.Value)
            {
                cutoffTime = _sessionStartedUtc;
            }

            var allPositionPackets = await _packetQueryService.GetMostRecentPositionPacketsAsync();

            var filteredPackets = allPositionPackets.Values
                .Where(r => !cutoffTime.HasValue || new DateTimeOffset(r.Timestamp, TimeSpan.Zero) >= cutoffTime.Value)
                .Where(r => r.PortId.HasValue && visiblePortIds.Contains(r.PortId.Value));

            var markers = new List<BeaconMarker>();

            foreach (var record in filteredPackets)
            {
                if (record.Position != null)
                {
                    var callsign = FormatCallsign(record.SourceBase, record.SourceSsid);
                    _logger.LogDebug(
                        "Adding map beacon for {Callsign} at Lat={Latitude:F5}, Lon={Longitude:F5}",
                        callsign,
                        record.Position.Location.Latitude,
                        record.Position.Location.Longitude);

                    markers.Add(CreateMarkerFromRecord(record));
                }
            }

            // Render live objects and items as named markers (killed reports are hidden).
            var liveObjects = (await _packetQueryService.GetMostRecentObjectsAsync())
                .Where(r => r.Object is { IsLive: true })
                .Where(r => !cutoffTime.HasValue || new DateTimeOffset(r.Timestamp, TimeSpan.Zero) >= cutoffTime.Value)
                .Where(r => r.PortId.HasValue && visiblePortIds.Contains(r.PortId.Value));

            foreach (var record in liveObjects)
            {
                var obj = record.Object!;
                markers.Add(CreateMarker(
                    obj.Location.Latitude,
                    obj.Location.Longitude,
                    obj.SymbolTable,
                    obj.SymbolCode,
                    obj.Name,
                    obj.Name));
            }

            var liveItems = (await _packetQueryService.GetMostRecentItemsAsync())
                .Where(r => r.Item is { IsLive: true })
                .Where(r => !cutoffTime.HasValue || new DateTimeOffset(r.Timestamp, TimeSpan.Zero) >= cutoffTime.Value)
                .Where(r => r.PortId.HasValue && visiblePortIds.Contains(r.PortId.Value));

            foreach (var record in liveItems)
            {
                var item = record.Item!;
                markers.Add(CreateMarker(
                    item.Location.Latitude,
                    item.Location.Longitude,
                    item.SymbolTable,
                    item.SymbolCode,
                    item.Name,
                    item.Name));
            }

            _markers = markers;

            // Bulk fetch all position trail packets once, then group by source client-side.
            // Note: trails intentionally NOT filtered by port (preserves existing behavior
            // where a station's trail includes packets heard on any port).
            var allTrailPackets = await _packetQueryService.GetPositionTrailPacketsAsync(cutoffTime);

            var trailsBySource = allTrailPackets
                .GroupBy(r => (r.SourceBase, r.SourceSsid))
                .Select(g => g
                    .OrderBy(r => r.Timestamp)
                    .TakeLast(MaxTrailPointsPerStation)
                    .ToList())
                .Where(trail => trail.Count > 1)
                .ToList();

            _trailFeatures.Clear();

            foreach (var trailPositions in trailsBySource)
            {
                var trailFeature = CreateTrailFeature(trailPositions);
                if (trailFeature != null)
                {
                    var latest = trailPositions[^1];
                    _trailFeatures[FormatCallsign(latest.SourceBase, latest.SourceSsid)] = trailFeature;
                }
            }

            RenderBeaconsLayer();
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

    private static GeometryFeature? CreateTrailFeature(List<PacketRecord> trail)
    {
        if (trail.Count < 2)
        {
            return null;
        }

        var recentTrail = new List<PacketRecord>();
        for (var index = trail.Count - 1; index >= 0; index--)
        {
            var packet = trail[index];
            if (packet.Position is null)
            {
                continue;
            }

            if (recentTrail.Count > 0)
            {
                var previous = recentTrail[^1].Position!.Location;
                var current = packet.Position.Location;
                if (GetDistanceMeters(previous.Latitude, previous.Longitude, current.Latitude, current.Longitude)
                    > MaxTrailJumpMeters)
                {
                    break;
                }
            }

            recentTrail.Add(packet);
        }

        recentTrail.Reverse();
        var coordinates = recentTrail
            .Select(p =>
            {
                var (x, y) = SphericalMercator.FromLonLat(
                    p.Position!.Location.Longitude,
                    p.Position.Location.Latitude);
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

    private static double GetDistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double earthRadiusMeters = 6_371_000;
        var latitudeDelta = DegreesToRadians(latitude2 - latitude1);
        var longitudeDelta = DegreesToRadians(longitude2 - longitude1);
        var sineLatitude = Math.Sin(latitudeDelta / 2);
        var sineLongitude = Math.Sin(longitudeDelta / 2);
        var a = sineLatitude * sineLatitude
            + Math.Cos(DegreesToRadians(latitude1))
            * Math.Cos(DegreesToRadians(latitude2))
            * sineLongitude * sineLongitude;
        return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    private static BeaconMarker CreateMarkerFromRecord(PacketRecord record)
    {
        var callsign = FormatCallsign(record.SourceBase, record.SourceSsid);
        var (x, y) = SphericalMercator.FromLonLat(
            record.Position!.Location.Longitude,
            record.Position!.Location.Latitude);
        return new BeaconMarker(
            new MPoint(x, y),
            record.Position.SymbolTable,
            record.Position.SymbolCode,
            callsign,
            callsign,
            null);
    }

    private static BeaconMarker CreateMarker(
        double latitude,
        double longitude,
        SymbolTable symbolTable,
        SymbolCode symbolCode,
        string label,
        string? objectName = null)
    {
        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        return new BeaconMarker(new MPoint(x, y), symbolTable, symbolCode, label, null, objectName);
    }

    /// <summary>
    /// Re-renders the beacons layer from the current marker set, clustering markers that
    /// fall into the same grid cell (sized ~40 screen pixels) into a single cluster feature.
    /// </summary>
    private void RenderBeaconsLayer()
    {
        _beaconsLayer.Clear();
        _clusteredCallsigns.Clear();

        if (_markers.Count == 0)
        {
            _beaconsLayer.DataHasChanged();
            RenderTrailsLayer();
            return;
        }

        // Before the first resolution is known, render every marker individually.
        if (double.IsNaN(_currentResolution))
        {
            foreach (var marker in _markers)
            {
                _beaconsLayer.Add(CreateLabeledFeature(
                    marker.Point,
                    marker.SymbolTable,
                    marker.SymbolCode,
                    marker.Label,
                    marker.Callsign,
                    marker.ObjectName));
            }

            _beaconsLayer.DataHasChanged();
            RenderTrailsLayer();
            return;
        }

        var cellSize = 40.0 * _currentResolution;
        var groups = MapClusterer.Cluster(_markers.Select(m => m.Point).ToList(), cellSize);

        foreach (var group in groups)
        {
            if (group.Count == 1)
            {
                var marker = _markers[group[0]];
                _beaconsLayer.Add(CreateLabeledFeature(
                    marker.Point,
                    marker.SymbolTable,
                    marker.SymbolCode,
                    marker.Label,
                    marker.Callsign,
                    marker.ObjectName));
            }
            else
            {
                foreach (var marker in group.Select(i => _markers[i]))
                {
                    if (marker.Callsign is not null)
                    {
                        _clusteredCallsigns.Add(marker.Callsign);
                    }
                }

                _beaconsLayer.Add(CreateClusterFeature(group.Select(i => _markers[i]).ToList()));
            }
        }

        _beaconsLayer.DataHasChanged();
        RenderTrailsLayer();
    }

    private void RenderTrailsLayer()
    {
        _trailsLayer.Clear();

        if (double.IsNaN(_currentResolution) ||
            _currentResolution > MaxTrailResolutionMetersPerPixel)
        {
            _trailsLayer.DataHasChanged();
            return;
        }

        foreach (var (callsign, feature) in _trailFeatures)
        {
            if (!_clusteredCallsigns.Contains(callsign))
            {
                _trailsLayer.Add(feature);
            }
        }

        _trailsLayer.DataHasChanged();
    }

    private static PointFeature CreateClusterFeature(IReadOnlyList<BeaconMarker> markers)
    {
        var centroid = new MPoint(
            markers.Average(m => m.Point.X),
            markers.Average(m => m.Point.Y));

        var symbolStyle = new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            Fill = new Brush(new Color(25, 118, 210, 220)),
            Outline = new Pen(Color.White, 2),
            SymbolScale = 0.7
        };

        var labelStyle = new LabelStyle
        {
            Text = markers.Count.ToString(),
            ForeColor = Color.White,
            BackColor = null,
            Halo = null,
            Font = new Font { FontFamily = "Arial", Size = 14, Bold = true }
        };

        var feature = new PointFeature(centroid)
        {
            Styles = [symbolStyle, labelStyle]
        };

        feature["Cluster"] = true;
        feature["Count"] = markers.Count;

        return feature;
    }

    /// <summary>
    /// Builds a labeled point feature for a station, object, or item.
    /// <paramref name="callsign"/> and <paramref name="objectName"/> are the
    /// click-to-navigate keys; stations carry a callsign while objects/items carry
    /// their name.
    /// </summary>
    private PointFeature CreateLabeledFeature(
        MPoint mapPoint,
        SymbolTable symbolTable,
        SymbolCode symbolCode,
        string label,
        string? callsign = null,
        string? objectName = null)
    {
        var symbol = new Symbol(symbolTable, symbolCode);

        var symbolKey = $"{symbolTable.ToChar()}{symbolCode.ToChar()}";
        if (!_symbolStyleCache.TryGetValue(symbolKey, out var cachedStyle) ||
            cachedStyle.symbol != symbol)
        {
            var imageStyle = _symbolConverter.CreateImageStyle(symbol, scale: 0.25);
            cachedStyle = (symbol, imageStyle);
            _symbolStyleCache[symbolKey] = cachedStyle;
        }

        var labelStyle = new LabelStyle
        {
            Text = label,
            Offset = new Offset(45, 0),
            Font = new Font { FontFamily = "Arial", Size = 14, Bold = true },
            ForeColor = Color.Black,
            BackColor = new Brush(new Color(255, 255, 255, 200)),
            Halo = new Pen(Color.White, 2)
        };

        var feature = new PointFeature(mapPoint)
        {
            Styles = [cachedStyle.imageStyle, labelStyle]
        };

        if (callsign is not null)
        {
            feature["Callsign"] = callsign;
        }

        if (objectName is not null)
        {
            feature["ObjectName"] = objectName;
        }

        return feature;
    }

    private static string FormatCallsign(string baseCallsign, AprsSsid? ssid)
    {
        return ssid.HasValue && ssid.Value != AprsSsid.PrimaryStation
            ? $"{baseCallsign}-{(byte)ssid.Value}"
            : baseCallsign;
    }

    /// <summary>
    /// Updates the current viewport resolution and re-clusters the beacons layer when the
    /// change is significant enough to alter clustering (5% threshold).
    /// </summary>
    public void SetResolution(double resolution)
    {
        if (_disposed || resolution <= 0)
        {
            return;
        }

        if (!double.IsNaN(_currentResolution) &&
            Math.Abs(resolution - _currentResolution) / _currentResolution < 0.05)
        {
            return;
        }

        _currentResolution = resolution;
        RunOnUi(RenderBeaconsLayer);
    }

    /// <summary>
    /// A beacon marker awaiting map rendering: its projected position, APRS symbol, label,
    /// and optional click-to-navigate callsign.
    /// </summary>
    private sealed record BeaconMarker(
        MPoint Point,
        SymbolTable SymbolTable,
        SymbolCode SymbolCode,
        string Label,
        string? Callsign,
        string? ObjectName);

    public void Dispose()
    {
        lock (_rebuildLock)
        {
            if (_disposed)
            {
                return;
            }

            // Unsubscribe BEFORE flagging disposed so no event handler can throw
            // into a publisher during the teardown window (M5).
            _packetStorageService.PacketStored -= OnPacketStored;
            _portService.PortsChanged -= OnPortsChanged;
            _disposed = true;
        }

        _beaconsLayer.Clear();
        _trailsLayer.Clear();
        _symbolStyleCache.Clear();
        _trailFeatures.Clear();
        _clusteredCallsigns.Clear();
        _symbolConverter.Dispose();
    }
}

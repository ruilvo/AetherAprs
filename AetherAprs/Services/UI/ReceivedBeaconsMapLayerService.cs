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
    private readonly IPacketViewFilterService _filterService;
    private readonly AprsSymbolMapConverter _symbolConverter;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<ReceivedBeaconsMapLayerService> _logger;
    private readonly WritableLayer _beaconsLayer;
    private readonly WritableLayer _trailsLayer;
    private readonly Dictionary<string, (Symbol symbol, ImageStyle imageStyle)> _symbolStyleCache = [];
    private readonly Dictionary<string, GeometryFeature> _trailFeatures = [];
    private readonly HashSet<string> _clusteredCallsigns = [with(StringComparer.OrdinalIgnoreCase)];
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
        IPacketViewFilterService filterService,
        IAprsSymbolBitmapProvider symbolBitmapProvider,
        IUiDispatcher uiDispatcher,
        ILogger<ReceivedBeaconsMapLayerService> logger)
    {
        _portService = portService ?? throw new ArgumentNullException(nameof(portService));
        _packetQueryService = packetQueryService ?? throw new ArgumentNullException(nameof(packetQueryService));
        _packetStorageService = packetStorageService ?? throw new ArgumentNullException(nameof(packetStorageService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _filterService = filterService ?? throw new ArgumentNullException(nameof(filterService));
        _symbolConverter = new AprsSymbolMapConverter(
            symbolBitmapProvider ?? throw new ArgumentNullException(nameof(symbolBitmapProvider)));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
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

        // Subscribe to events that should trigger map updates
        _packetStorageService.PacketStored += OnPacketStored;
        _filterService.FiltersChanged += OnFiltersChanged;
    }

    private void OnPacketStored(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        ScheduleRebuild();
    }

    private void OnFiltersChanged(object? sender, EventArgs e)
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
        bool startRequested = false;

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
            bool rerun = false;
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

    private void RunOnUi(Action action) => _uiDispatcher.Post(action);

    private async Task RebuildVisibleLayersAsync()
    {
        try
        {
            // Use shared filter service for port visibility and time filtering
            HashSet<Guid> visiblePortIds = _filterService.GetVisiblePortIds();
            DateTimeOffset? cutoffTime = _filterService.GetCutoffTime();

            IReadOnlyDictionary<string, PacketRecord> allPositionPackets = await _packetQueryService.GetMostRecentPositionPacketsAsync();

            IEnumerable<PacketRecord> filteredPackets = allPositionPackets.Values
                .Where(r => !cutoffTime.HasValue || new DateTimeOffset(r.Timestamp, TimeSpan.Zero) >= cutoffTime.Value)
                .Where(r => r.PortId.HasValue && visiblePortIds.Contains(r.PortId.Value));

            List<BeaconMarker> markers = [];

            foreach (PacketRecord? record in filteredPackets)
            {
                if (record.Position != null)
                {
                    string callsign = AprsSsidExtensions.FormatCallsign(record.SourceBase, record.SourceSsid);
                    _logger.LogDebug(
                        "Adding map beacon for {Callsign} at Lat={Latitude:F5}, Lon={Longitude:F5}",
                        callsign,
                        record.Position.Location.Latitude,
                        record.Position.Location.Longitude);

                    markers.Add(CreateMarkerFromRecord(record));
                }
            }

            // Render live objects and items as named markers (killed reports are hidden).
            IEnumerable<PacketRecord> liveObjects = (await _packetQueryService.GetMostRecentObjectsAsync())
                .Where(r => r.Object is { IsLive: true })
                .Where(r => !cutoffTime.HasValue || new DateTimeOffset(r.Timestamp, TimeSpan.Zero) >= cutoffTime.Value)
                .Where(r => r.PortId.HasValue && visiblePortIds.Contains(r.PortId.Value));

            foreach (PacketRecord? record in liveObjects)
            {
                ObjectDataRecord obj = record.Object!;
                markers.Add(CreateMarker(
                    obj.Location.Latitude,
                    obj.Location.Longitude,
                    obj.SymbolTable,
                    obj.SymbolCode,
                    obj.Name,
                    obj.Name));
            }

            IEnumerable<PacketRecord> liveItems = (await _packetQueryService.GetMostRecentItemsAsync())
                .Where(r => r.Item is { IsLive: true })
                .Where(r => !cutoffTime.HasValue || new DateTimeOffset(r.Timestamp, TimeSpan.Zero) >= cutoffTime.Value)
                .Where(r => r.PortId.HasValue && visiblePortIds.Contains(r.PortId.Value));

            foreach (PacketRecord? record in liveItems)
            {
                ItemDataRecord item = record.Item!;
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
            IReadOnlyList<PacketRecord> allTrailPackets = await _packetQueryService.GetPositionTrailPacketsAsync(cutoffTime);

            List<List<PacketRecord>> trailsBySource = [.. allTrailPackets
                .GroupBy(r => (r.SourceBase, r.SourceSsid))
                .Select(g => g
                    .OrderBy(r => r.Timestamp)
                    .TakeLast(MaxTrailPointsPerStation)
                    .ToList())
                .Where(trail => trail.Count > 1)];

            _trailFeatures.Clear();

            foreach (List<PacketRecord> trailPositions in trailsBySource)
            {
                GeometryFeature? trailFeature = CreateTrailFeature(trailPositions);
                if (trailFeature != null)
                {
                    PacketRecord latest = trailPositions[^1];
                    _trailFeatures[AprsSsidExtensions.FormatCallsign(latest.SourceBase, latest.SourceSsid)] = trailFeature;
                }
            }

            RenderBeaconsLayer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild map layers");
        }
    }

    private static GeometryFeature? CreateTrailFeature(List<PacketRecord> trail)
    {
        if (trail.Count < 2)
        {
            return null;
        }

        List<PacketRecord> recentTrail = [];
        for (int index = trail.Count - 1; index >= 0; index--)
        {
            PacketRecord packet = trail[index];
            if (packet.Position is null)
            {
                continue;
            }

            if (recentTrail.Count > 0)
            {
                Geo.Coordinate previous = recentTrail[^1].Position!.Location;
                Geo.Coordinate current = packet.Position.Location;
                if (GetDistanceMeters(previous.Latitude, previous.Longitude, current.Latitude, current.Longitude)
                    > MaxTrailJumpMeters)
                {
                    break;
                }
            }

            recentTrail.Add(packet);
        }

        recentTrail.Reverse();
        Coordinate[] coordinates = [.. recentTrail
            .Select(p =>
            {
                (double x, double y) = SphericalMercator.FromLonLat(
                    p.Position!.Location.Longitude,
                    p.Position.Location.Latitude);
                return new Coordinate(x, y);
            })];

        if (coordinates.Length < 2)
        {
            return null;
        }

        LineString lineString = new(coordinates);
        GeometryFeature feature = new() { Geometry = lineString };

        feature.Styles.Add(new VectorStyle
        {
            Line = new Pen(Color.FromArgb(200, 0, 120, 215), 2)
        });

        return feature;
    }

    private static double GetDistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double earthRadiusMeters = 6_371_000;
        double latitudeDelta = DegreesToRadians(latitude2 - latitude1);
        double longitudeDelta = DegreesToRadians(longitude2 - longitude1);
        double sineLatitude = Math.Sin(latitudeDelta / 2);
        double sineLongitude = Math.Sin(longitudeDelta / 2);
        double a = sineLatitude * sineLatitude
            + Math.Cos(DegreesToRadians(latitude1))
            * Math.Cos(DegreesToRadians(latitude2))
            * sineLongitude * sineLongitude;
        return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    private static BeaconMarker CreateMarkerFromRecord(PacketRecord record)
    {
        string callsign = AprsSsidExtensions.FormatCallsign(record.SourceBase, record.SourceSsid);
        (double x, double y) = SphericalMercator.FromLonLat(
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
        (double x, double y) = SphericalMercator.FromLonLat(longitude, latitude);
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
            foreach (BeaconMarker marker in _markers)
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

        double cellSize = 40.0 * _currentResolution;
        IReadOnlyList<IReadOnlyList<int>> groups = MapClusterer.Cluster([.. _markers.Select(m => m.Point)], cellSize);

        foreach (IReadOnlyList<int> group in groups)
        {
            if (group.Count == 1)
            {
                BeaconMarker marker = _markers[group[0]];
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
                foreach (BeaconMarker? marker in group.Select(i => _markers[i]))
                {
                    if (marker.Callsign is not null)
                    {
                        _clusteredCallsigns.Add(marker.Callsign);
                    }
                }

                _beaconsLayer.Add(CreateClusterFeature([.. group.Select(i => _markers[i])]));
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

        foreach ((string? callsign, GeometryFeature? feature) in _trailFeatures)
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
        MPoint centroid = new(
            markers.Average(m => m.Point.X),
            markers.Average(m => m.Point.Y));

        SymbolStyle symbolStyle = new()
        {
            SymbolType = SymbolType.Ellipse,
            Fill = new Brush(new Color(25, 118, 210, 220)),
            Outline = new Pen(Color.White, 2),
            SymbolScale = 0.7
        };

        LabelStyle labelStyle = new()
        {
            Text = markers.Count.ToString(),
            ForeColor = Color.White,
            BackColor = null,
            Halo = null,
            Font = new Font { FontFamily = "Arial", Size = 14, Bold = true }
        };

        PointFeature feature = new(centroid)
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
        Symbol symbol = new(symbolTable, symbolCode);

        string symbolKey = $"{symbolTable.ToChar()}{symbolCode.ToChar()}";
        if (!_symbolStyleCache.TryGetValue(symbolKey, out (Symbol symbol, ImageStyle imageStyle) cachedStyle) ||
            cachedStyle.symbol != symbol)
        {
            ImageStyle imageStyle = _symbolConverter.CreateImageStyle(symbol, scale: 0.25);
            cachedStyle = (symbol, imageStyle);
            _symbolStyleCache[symbolKey] = cachedStyle;
        }

        LabelStyle labelStyle = new()
        {
            Text = label,
            Offset = new Offset(45, 0),
            Font = new Font { FontFamily = "Arial", Size = 14, Bold = true },
            ForeColor = Color.Black,
            BackColor = new Brush(new Color(255, 255, 255, 200)),
            Halo = new Pen(Color.White, 2)
        };

        PointFeature feature = new(mapPoint)
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
            _filterService.FiltersChanged -= OnFiltersChanged;
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

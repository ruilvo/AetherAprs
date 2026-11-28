// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Configuration.Settings;
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

    /// <summary>
    /// Gets the callsign the operator last selected on the map, or <see langword="null"/>
    /// when nothing is selected.
    /// </summary>
    string? SelectedCallsign { get; }

    /// <summary>
    /// Records the station the operator selected. The selection is not tied to a
    /// rendered feature, so it survives the rebuilds that follow new packets and
    /// filter changes.
    /// </summary>
    /// <param name="callsign">The selected callsign, or <see langword="null"/> to clear.</param>
    void SelectStation(string? callsign);
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
    private CancellationTokenSource? _rebuildCancellation;
    private bool _disposed;
    private List<BeaconMarker> _markers = [];
    private double _currentResolution = double.NaN;
    private string? _ownCallsign;

    public ILayer BeaconsLayer => _beaconsLayer;
    public ILayer TrailsLayer => _trailsLayer;
    public string? SelectedCallsign { get; private set; }

    public ReceivedBeaconsMapLayerService(
        IPortService portService,
        IPacketQueryService packetQueryService,
        IPacketStorageService packetStorageService,
        IConfigurationService configurationService,
        IPacketViewFilterService filterService,
        AprsSymbolMapConverter symbolConverter,
        IUiDispatcher uiDispatcher,
        ILogger<ReceivedBeaconsMapLayerService> logger)
    {
        _portService = portService ?? throw new ArgumentNullException(nameof(portService));
        _packetQueryService = packetQueryService ?? throw new ArgumentNullException(nameof(packetQueryService));
        _packetStorageService = packetStorageService ?? throw new ArgumentNullException(nameof(packetStorageService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _filterService = filterService ?? throw new ArgumentNullException(nameof(filterService));
        _symbolConverter = symbolConverter ?? throw new ArgumentNullException(nameof(symbolConverter));
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
    /// arriving during a rebuild cancel the in-flight run and schedule exactly one
    /// replacement (H2, R6.4). This prevents per-packet full rebuilds under an active
    /// APRS-IS feed, overlapping rebuilds racing on the shared layers, and a
    /// superseded run continuing against a dataset and filter set that no longer
    /// apply.
    /// </summary>
    private void ScheduleRebuild()
    {
        bool startRequested = false;
        CancellationTokenSource? superseded = null;

        lock (_rebuildLock)
        {
            if (_disposed)
            {
                return;
            }

            if (_rebuildRunning)
            {
                _rebuildPending = true;
                superseded = _rebuildCancellation;
            }
            else
            {
                _rebuildRunning = true;
                _rebuildPending = true;
                startRequested = true;
            }
        }

        if (superseded is not null)
        {
            // Cancel outside the lock: registered callbacks can run inline and must
            // not observe (or block on) the rebuild latch.
            Cancel(superseded);
            return;
        }

        if (startRequested)
        {
            RunOnUi(() => _ = RebuildLoopAsync());
        }
    }

    /// <summary>
    /// Cancels a superseded rebuild. The source stays owned by the run that is
    /// unwinding, which disposes it, so the token a query is still holding stays
    /// usable until that query returns.
    /// </summary>
    private void Cancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The superseded run finished and disposed its source first; there is
            // nothing left to cancel.
        }
        catch (AggregateException ex)
        {
            // A cancellation callback threw. The rebuild is abandoned either way.
            _logger.LogWarning(ex, "A rebuild cancellation callback failed");
        }
    }

    private async Task RebuildLoopAsync()
    {
        while (true)
        {
            CancellationTokenSource cancellation = new();
            lock (_rebuildLock)
            {
                if (_disposed)
                {
                    _rebuildRunning = false;
                    cancellation.Dispose();
                    return;
                }

                _rebuildPending = false;
                _rebuildCancellation = cancellation;
            }

            try
            {
                await RebuildVisibleLayersAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // A newer rebuild superseded this one; it is started below.
            }
            finally
            {
                lock (_rebuildLock)
                {
                    if (ReferenceEquals(_rebuildCancellation, cancellation))
                    {
                        _rebuildCancellation = null;
                    }

                    cancellation.Dispose();
                }
            }

            lock (_rebuildLock)
            {
                if (!_rebuildPending || _disposed)
                {
                    _rebuildRunning = false;
                    return;
                }
            }
        }
    }

    private void RunOnUi(Action action) => _uiDispatcher.Post(action);

    private async Task RebuildVisibleLayersAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Use shared filter service for port visibility and time filtering
            HashSet<Guid> visiblePortIds = _filterService.GetVisiblePortIds();
            DateTimeOffset? cutoffTime = _filterService.GetCutoffTime();

            // The operator's own station is part of the published settings snapshot,
            // so it is re-read on every rebuild and stays correct after a callsign
            // change without restarting the map.
            AprsSettings aprs = _configurationService.Current.Aprs;
            string ownCallsign = AprsSsidExtensions.FormatCallsign(aprs.Callsign, aprs.Ssid);
            _ownCallsign = ownCallsign;

            IReadOnlyDictionary<string, PacketRecord> allPositionPackets = await _packetQueryService
                .GetMostRecentPositionPacketsAsync(cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<PacketRecord> filteredPackets = allPositionPackets.Values
                .Where(r => !cutoffTime.HasValue || new DateTimeOffset(r.Timestamp, TimeSpan.Zero) >= cutoffTime.Value)
                .Where(r => r.PortId.HasValue && visiblePortIds.Contains(r.PortId.Value));

            List<BeaconMarker> markers = [];

            foreach (PacketRecord? record in filteredPackets)
            {
                if (record.Position != null)
                {
                    string callsign = AprsSsidExtensions.FormatCallsign(record.SourceBase, record.SourceSsid);
                    _logger.LogDebug("Adding map beacon for {Callsign}", callsign);

                    markers.Add(CreateMarkerFromRecord(record));
                }
            }

            // Render live objects and items as named markers (killed reports are hidden).
            IEnumerable<PacketRecord> liveObjects = (await _packetQueryService
                    .GetMostRecentObjectsAsync(cancellationToken: cancellationToken))
                .Where(r => r.Object is { IsLive: true })
                .Where(r => !cutoffTime.HasValue || new DateTimeOffset(r.Timestamp, TimeSpan.Zero) >= cutoffTime.Value)
                .Where(r => r.PortId.HasValue && visiblePortIds.Contains(r.PortId.Value));

            foreach (PacketRecord? record in liveObjects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDataRecord obj = record.Object!;
                markers.Add(CreateMarker(
                    obj.Location.Latitude,
                    obj.Location.Longitude,
                    obj.SymbolTable,
                    obj.SymbolCode,
                    obj.Name,
                    obj.Name));
            }

            IEnumerable<PacketRecord> liveItems = (await _packetQueryService
                    .GetMostRecentItemsAsync(cancellationToken: cancellationToken))
                .Where(r => r.Item is { IsLive: true })
                .Where(r => !cutoffTime.HasValue || new DateTimeOffset(r.Timestamp, TimeSpan.Zero) >= cutoffTime.Value)
                .Where(r => r.PortId.HasValue && visiblePortIds.Contains(r.PortId.Value));

            foreach (PacketRecord? record in liveItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ItemDataRecord item = record.Item!;
                markers.Add(CreateMarker(
                    item.Location.Latitude,
                    item.Location.Longitude,
                    item.SymbolTable,
                    item.SymbolCode,
                    item.Name,
                    item.Name));
            }

            // Bulk fetch all position trail packets once, then group by source client-side.
            // Note: trails intentionally NOT filtered by port (preserves existing behavior
            // where a station's trail includes packets heard on any port).
            IReadOnlyList<PacketRecord> allTrailPackets = await _packetQueryService
                .GetPositionTrailPacketsAsync(cutoffTime, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            List<List<PacketRecord>> trailsBySource = [.. allTrailPackets
                .GroupBy(r => (r.SourceBase, r.SourceSsid))
                .Select(g => g
                    .OrderBy(r => r.Timestamp)
                    .TakeLast(MaxTrailPointsPerStation)
                    .ToList())
                .Where(trail => trail.Count > 1)];

            Dictionary<string, GeometryFeature> trailFeatures = [];

            foreach (List<PacketRecord> trailPositions in trailsBySource)
            {
                GeometryFeature? trailFeature = CreateTrailFeature(trailPositions);
                if (trailFeature != null)
                {
                    PacketRecord latest = trailPositions[^1];
                    trailFeatures[AprsSsidExtensions.FormatCallsign(latest.SourceBase, latest.SourceSsid)] = trailFeature;
                }
            }

            // Do not publish a dataset that was superseded while it was assembling;
            // the previous marker and trail set stays on the map until the replacement
            // run commits its own.
            cancellationToken.ThrowIfCancellationRequested();

            _markers = markers;
            _trailFeatures.Clear();
            foreach ((string callsign, GeometryFeature feature) in trailFeatures)
            {
                _trailFeatures[callsign] = feature;
            }

            RenderBeaconsLayer();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer rebuild: drop this run's partial dataset instead
            // of rendering it, and let the loop start the replacement.
            throw;
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
    /// The selected station and the operator's own station are always drawn as
    /// individual, flagged features so that neither disappears into a cluster when the
    /// map is zoomed out or a new packet arrives.
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
                _beaconsLayer.Add(CreateMarkerFeature(marker));
            }

            _beaconsLayer.DataHasChanged();
            RenderTrailsLayer();
            return;
        }

        double cellSize = 40.0 * _currentResolution;

        // Pinned markers keep their own feature: they are the ones the operator is
        // looking at, so a cluster bubble must not replace them. They are also kept out
        // of the clustering input entirely, so a pinned marker never drags the bubble
        // away from the stations it groups.
        List<BeaconMarker> pinned = [];
        List<BeaconMarker> clusterable = [];

        foreach (BeaconMarker marker in _markers)
        {
            if (IsPinned(marker))
            {
                pinned.Add(marker);
            }
            else
            {
                clusterable.Add(marker);
            }
        }

        foreach (BeaconMarker marker in pinned)
        {
            _beaconsLayer.Add(CreateMarkerFeature(marker));
        }

        IReadOnlyList<IReadOnlyList<int>> groups = MapClusterer.Cluster(
            [.. clusterable.Select(marker => marker.Point)],
            cellSize);

        foreach (IReadOnlyList<int> group in groups)
        {
            if (group.Count == 1)
            {
                _beaconsLayer.Add(CreateMarkerFeature(clusterable[group[0]]));
            }
            else
            {
                List<BeaconMarker> grouped = [.. group.Select(index => clusterable[index])];
                foreach (BeaconMarker marker in grouped)
                {
                    if (marker.Callsign is { } callsign)
                    {
                        _clusteredCallsigns.Add(callsign);
                    }
                }

                _beaconsLayer.Add(CreateClusterFeature(grouped));
            }
        }

        _beaconsLayer.DataHasChanged();
        RenderTrailsLayer();
    }

    private PointFeature CreateMarkerFeature(BeaconMarker marker)
    {
        PointFeature feature = CreateLabeledFeature(
            marker.Point,
            marker.SymbolTable,
            marker.SymbolCode,
            marker.Label,
            marker.Callsign,
            marker.ObjectName);

        if (IsPinned(marker))
        {
            // The view can highlight these without scanning the marker set again.
            feature["Pinned"] = true;
        }

        return feature;
    }

    /// <summary>
    /// A marker is pinned when it is the operator's selection or its own station.
    /// </summary>
    private bool IsPinned(BeaconMarker marker) =>
        IsStationPinned(marker.Callsign, SelectedCallsign) ||
        IsStationPinned(marker.Callsign, _ownCallsign);

    private static bool IsStationPinned(string? callsign, string? pinned) =>
        callsign is not null &&
        pinned is { Length: > 0 } &&
        string.Equals(callsign, pinned, StringComparison.OrdinalIgnoreCase);

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
    /// Records the selected station and re-renders so it stops being hidden inside a
    /// cluster. The selection itself lives outside the marker set, which is rebuilt
    /// from the database on every packet, so it survives those rebuilds.
    /// </summary>
    public void SelectStation(string? callsign)
    {
        if (_disposed)
        {
            return;
        }

        string? normalized = string.IsNullOrWhiteSpace(callsign) ? null : callsign.Trim();
        if (string.Equals(SelectedCallsign, normalized, StringComparison.Ordinal))
        {
            return;
        }

        SelectedCallsign = normalized;
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
        CancellationTokenSource? running = null;

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
            running = _rebuildCancellation;
            _rebuildPending = false;
        }

        if (running is not null)
        {
            // Stop an in-flight rebuild from finishing against the layers this method
            // is about to clear. The run owns and disposes the source.
            Cancel(running);
        }

        _beaconsLayer.Clear();
        _trailsLayer.Clear();
        _symbolStyleCache.Clear();
        _trailFeatures.Clear();
        _clusteredCallsigns.Clear();
        // The converter is a shared application-lifetime singleton; it is not
        // disposed here.
    }
}

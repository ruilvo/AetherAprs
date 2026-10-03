// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.Configuration;
using AetherAprs.Data;
using Avalonia;
using Avalonia.Threading;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Nts;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels;

/// <summary>
/// Manages the display of received APRS beacons on a Mapsui map layer.
/// Queries packet data from the database and updates map features with APRS symbols.
/// Port ShowOnMap is a visual filter only.
/// </summary>
public sealed class ReceivedBeaconsViewModel : IDisposable
{
    private readonly IPortService _portService;
    private readonly IPacketQueryService _packetQueryService;
    private readonly IPacketStorageService _packetStorageService;
    private readonly IConfigurationService _configurationService;
    private readonly AprsSymbolMapConverter _symbolConverter;
    private readonly ILogger<ReceivedBeaconsViewModel> _logger;
    private readonly WritableLayer _beaconsLayer;
    private readonly WritableLayer _trailsLayer;
    private readonly Dictionary<string, (Symbol symbol, ImageStyle imageStyle)> _symbolStyleCache = new();
    private bool _disposed;

    /// <summary>
    /// Gets the layer containing all received beacon features.
    /// Returns ILayer to prevent external modification of the internal WritableLayer.
    /// </summary>
    public ILayer BeaconsLayer => _beaconsLayer;

    /// <summary>
    /// Gets the layer containing position trails.
    /// </summary>
    public ILayer TrailsLayer => _trailsLayer;

    public ReceivedBeaconsViewModel(
        IPortService portService,
        IPacketQueryService packetQueryService,
        IPacketStorageService packetStorageService,
        IConfigurationService configurationService,
        IAprsSymbolBitmapProvider symbolBitmapProvider,
        ILogger<ReceivedBeaconsViewModel> logger)
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
            // Mapsui layers default to a white VectorStyle with a grey outline.
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
        RunOnUi(() => _ = RebuildVisibleLayerAsync());
    }

    private void OnPortsChanged(object? sender, EventArgs e)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RunOnUi(() => _ = RebuildVisibleLayerAsync());
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

    private async Task RebuildVisibleLayerAsync()
    {
        try
        {
            var visiblePortIds = _portService.Ports
                .Where(p => p.ShowOnMap)
                .Select(p => p.Id)
                .ToHashSet();

            // Get time range filter from configuration
            var timeRange = _configurationService.Settings.Aprs.DisplayTimeRange;
            var customHours = _configurationService.Settings.Aprs.CustomDisplayTimeRangeHours;
            var cutoffTime = GetCutoffTime(timeRange, customHours);

            // Get most recent position packets from database
            var allPositionPackets = await _packetQueryService.GetMostRecentPositionPacketsAsync();
            
            // Apply time filter
            var filteredPackets = allPositionPackets.Values
                .Where(r => !cutoffTime.HasValue || new DateTimeOffset(r.Timestamp, TimeSpan.Zero) >= cutoffTime.Value);

            // Apply port filter - only include packets from visible ports
            // If no ports are visible, show nothing
            filteredPackets = filteredPackets.Where(r => r.PortId.HasValue && visiblePortIds.Contains(r.PortId.Value));

            var packetRecords = filteredPackets.ToList();

            // Clear layers
            _beaconsLayer.Clear();
            _trailsLayer.Clear();

            // Add markers for latest positions
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

            // Add trails - query historical positions from database for each visible station
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
            _ => now.AddDays(-1) // Default to last day
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

        // Get symbol from record or use default
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
            ["Callsign"] = callsign // Store for click handling
        };
    }

    private static string FormatCallsign(string baseCallsign, AprsSsid? ssid)
    {
        return ssid.HasValue && ssid.Value != AprsSsid.PrimaryStation
            ? $"{baseCallsign}-{(byte)ssid.Value}"
            : baseCallsign;
    }

    /// <inheritdoc />
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
